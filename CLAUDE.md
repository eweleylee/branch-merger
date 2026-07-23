# Branch Merger — project guide

A small full-stack developer tool for merging one Git branch into another —
instantly or on a schedule. **C# / ASP.NET Core (.NET 8) backend + Vue 3 (Vite)
frontend.** Single-instance, self-hosted, no database.

This file orients an AI assistant or new developer. For end-user setup see `README.md`.

---

## What it does
- Pick a **source** branch (merge FROM) and **target** branch (merge INTO), e.g. `master → feature/x`.
- **Merge now**, or schedule it: **once** (a date/time) or **recurring** (a cron expression, UTC).
- The backend **fetches branches continuously in the background** so the dropdowns are always current.
- Schedules run **server-side**, firing even if no browser tab is open.
- Merge conflicts (and failed scheduled runs) raise an **in-app notification** (the 🔔 bell).
- Optionally **push** the target to the remote after a successful merge.
- Checks **GitHub Releases** for a newer version and shows an "update available" banner.

## The actual merge (in `GitService.MergeAsync`)
For `source → target`, serialized behind a single lock:
```
git fetch <remote> --prune
git checkout -B <target> <remote>/<target>   # ⚠ resets local target to match remote
git merge --no-edit <remote>/<source>
git push <remote> <target>                    # only if push == true
```
On conflict: capture unmerged files (`git diff --name-only --diff-filter=U`), run
`git merge --abort`, return `IsConflict = true` with the file list. **Nothing is pushed.**

> Because of the `checkout -B` reset, the app must point at a **dedicated working clone**
> it owns — never a repo edited by hand. `git` must have **non-interactive credentials**
> (SSH agent / cached HTTPS token / PAT) so background fetch/clone/push work without prompts.

---

## Architecture

### Backend (`backend/`, .NET 8, ASP.NET Core Web API)
Single dependency: **Cronos** (cron parsing). In production the API also **serves the
built Vue app** as static files (single-server mode) on `http://localhost:5080`.

**Program.cs** — DI registration, CORS (dev), static file serving + SPA fallback,
auto-opens the browser on start when not in Development.

**Services/** (all singletons; hold shared state)
- `AppPaths` — resolves the **per-user data directory** (`%APPDATA%/BranchMerger`,
  `~/.config/BranchMerger`), creates it, and migrates any legacy data files that an
  older build left next to the executable. **Data lives here, NOT in the program folder**,
  so rebuilds/updates never wipe settings.
- `AppSettingsStore` — live, editable settings persisted to `settings.json`; seeded once
  from `appsettings.json` on first run. Swapped atomically by reference on update.
- `IGitService` / `GitService` — shells out to the real `git` CLI (reuses machine
  credentials). **One `SemaphoreSlim` serializes all git ops** so fetch/merge/clone never
  collide (the clone can only be on one branch at a time). Methods: `FetchAsync`,
  `GetBranchesAsync`, `MergeAsync`, `GetRepoStatusAsync`, `EnsureRepositoryAsync` (clone).
- `BranchCache` + `BranchFetchBackgroundService` — the fetcher polls `git fetch` on the
  configured interval and refreshes the cache; controllers read the cache (fast).
- `ScheduleStore` — schedules persisted to `schedules.json`.
- `SchedulerBackgroundService` — ticks every 15s; runs due schedules ordered by
  `NextRunUtc` then `Order`; notifies on conflict/failure. Cron next-occurrence via Cronos.
- `NotificationStore` — capped in-app feed persisted to `notifications.json`.
- `NotificationChannels` (`INotificationChannel` + `InAppChannel`) — **in-app only**
  (webhook/email were removed). `NotificationService` fans out (currently just in-app).
- `UpdateService` — queries GitHub `releases/latest`, compares to the app version,
  caches ~6h.

**Controllers/** (`api/...`)
- `BranchesController` — `GET /api/branches` (cache), `POST /api/branches/refresh`.
- `MergeController` — `POST /api/merge` (instant); notifies on conflict.
- `SchedulesController` — CRUD + `PUT /api/schedules/reorder` + `POST /{id}/toggle`.
- `NotificationsController` — feed, read-all, clear, `POST /api/notifications/test`.
- `SettingsController` — `GET/PUT /api/settings`, `GET /api/settings/repo-status`, `POST /api/settings/clone`.
- `UpdateController` — `GET /api/update`, `POST /api/update/check`.

**Models/** — `GitRepositoryConfig`, `AppSettings`, `RepoStatus`, `BranchInfo`,
`MergeRequest`, `MergeResult` (`IsConflict`, `ConflictedFiles`), `MergeSchedule`
(`Type` = `Once|Cron`, `Order` is the same-time tiebreaker), `CreateScheduleDto`,
`Notification` (`Level` = `Info|Warning|Error`).

### Frontend (`frontend/`, Vue 3 + Vite)
Deps: `vue`, `cronstrue`; dev: `vite`, `@vitejs/plugin-vue`. Dark theme via CSS
variables in `style.css` (`--panel`, `--panel-2`, `--border`, `--text`, `--muted`,
`--accent`, `--accent2`, `--danger`, `--warn`).

- `api.js` — thin `fetch` wrapper; on non-2xx it throws an `Error` with `.data` set to
  the response body (so callers can read `err.data.conflictedFiles` etc.).
- `cron.js` — `describeCron(expr)` = **cronstrue with a hand-rolled fallback** (auto, no
  UI toggle); plus `nextRun`/`formatNext` (client-side minute scan; numeric fields only).
- `App.vue` — layout, polls every 10s (`getBranches`/`getSchedules`/`getNotifications`),
  banners (update-available, repo-not-ready), header (bell + gear).
- `components/`
  - `MergePanel.vue` — source/target via `BranchSelect`, push toggle, mode segments
    (now / once / cron), live cron echo + next-run, merge/schedule actions, result with
    conflicted-file list.
  - `BranchSelect.vue` — **searchable, accessible combobox** (type to filter on short name,
    arrow/Enter/Esc, click-outside, ARIA); **strict** (only real branches); shows names only.
  - `ScheduleList.vue` — rows **grouped by run time**; **drag-to-reorder within a same-time
    group** (order only matters for same-time schedules); horizontally scrollable on mobile.
  - `NotificationBell.vue` — in-app feed dropdown (✕ + backdrop close, responsive).
  - `SettingsPanel.vue` — **Repository section only** (path, URL, remote, fetch interval,
    Check status / Clone). Webhook/email sections were removed.

Vite dev-proxies `/api` → `localhost:5080`. In production there's no proxy (same origin).

### Demo (separate project, shipped as `branch-merger-demo/`)
The **same Vue frontend** wired to an in-memory mock backend
(`src/mock/mockBackend.js`) — no C#, no git, in-browser scheduler. Runs with
`npm install && npm run dev`. Only `api.js` differs (delegates to the mock); every
component is identical to production. Keep it in sync when components change.

---

## Build & run

**Dev (two servers, hot reload):**
```
cd backend  && dotnet run          # API on :5080
cd frontend && npm install && npm run dev   # UI on :5173 (proxies /api)
```

**Production (one folder, single-server):**
```
./build.sh            # or .\build.ps1   (framework-dependent; needs .NET 8 runtime)
./build.sh osx-arm64  # or a RID for a self-contained single-file build
```
Produces `output/` (published API + `wwwroot/` with the built UI) and a launcher
(`Start.bat` / `Start.command` / `start.sh`). Serves UI + API on `:5080` and opens the
browser. **`build.sh` deletes `output/` first — that's safe now because data lives in the
per-user directory, not `output/`.**

Config: **everything is edited in the ⚙️ Settings screen at runtime** (persisted to
`settings.json`); `appsettings.json` only seeds first-run defaults.

---

## Current default config (`backend/appsettings.json`)
- `Git.RepositoryUrl` = "" (set per machine in the Settings screen)
- `Git.RemoteName` = `origin`
- `Git.FetchIntervalSeconds` = 60
- `Git.RepositoryPath` = "" (must be set per machine — the dedicated clone)
- `DataDirectory` = "" (empty ⇒ per-user default)
- `UpdateCheck.GitHubRepo` = "" (**set to `owner/repo` to enable the update banner**)
- `UpdateCheck.CurrentVersion` = "" (empty ⇒ assembly version, `<Version>` in the csproj = 1.0.0)

## Conventions & gotchas
- **Dedicated clone only** (merge resets local target). **Non-interactive git creds required.**
- All git ops are **serialized**; there is no per-repo concurrency yet.
- Cron is **5-field, UTC**. `nextRun` in the UI handles numeric/list/range/step fields but
  not name-based days (`MON`) or `L`/`#`; cronstrue still describes those in words.
- Persistent state = three JSON files in the per-user data dir. **No database.**
- Notifications are **in-app only** (webhook/email intentionally removed).
- The TFS remote URL needs a **PAT or cached Windows auth** for non-interactive push/fetch.
- When editing shared components, **update the demo copy too** (or re-copy).

---

## Environment note for Claude Code
Unlike the chat this was built in, Claude Code can actually **compile the backend**
(`dotnet build`) and run everything — please do, since the C# was written but not
compiled in-chat (no .NET SDK there). Verify with `dotnet build` in `backend/` and
`npm run build` in `frontend/` after changes.

---

## Open work (discussed, not yet built) — rough priority
1. **Windows Service support** — `UseWindowsService()`, suppress browser-launch + switch to
   file logging when running as a service, `install-service.ps1` / `uninstall-service.ps1`.
   Solves "hide the console" + "start on Windows startup". Run the service under a user
   account that has the right git credentials (LocalSystem won't). **(explicitly queued)**
2. **Conflict handling beyond abort** — either "push to `automerge/...` branch + notify" or an
   in-UI resolver with an `AwaitingResolution` repo state (needs a repo-wide lock, or
   `git worktree` for concurrency). Notifications already carry the conflicted files.
3. **Stop-on-failure toggle per same-time group** — when an earlier merge in a group fails,
   skip the rest (for dependent chains like `master→develop` then `develop→feature`).
4. **Multi-operation batch merge** — `List<MergeOperation>` per schedule (fan-out / fan-in),
   sequential, with the stop-vs-continue policy above.
5. **Multi-repo profiles** — list of repos, each with its own clone/remote/schedules
   (settings store becomes a list; git ops keyed per repo).
6. **SQLite/EF Core** alternative to the JSON stores — only needed for multi-instance or
   history/reporting. Storage is already isolated behind the three `*Store` classes.
7. **Docker** packaging (mount the clone + data dir as volumes).
8. Minor: hide number-input spinners; auto-apply updates (currently notify + link only).
