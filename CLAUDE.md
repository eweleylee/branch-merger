# Branch Merger — project guide

A small full-stack developer tool for merging one Git branch into another —
instantly or on a schedule. **C# / ASP.NET Core (.NET 8) backend + Vue 3 (Vite)
frontend.** Single-instance, self-hosted, no database.

This file orients an AI assistant or new developer. For end-user setup see `README.md`.

---

## What it does
- Pick a **source** branch (merge FROM) and **target** branch (merge INTO), e.g. `master → feature/x`.
- **Merge now**, or schedule it: **once** (a date/time) or **recurring** (a cron expression,
  interpreted in the **server PC's local timezone**).
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
git checkout <Git.DefaultBranch>              # best-effort: rest on master, never on target
git branch -D <target>                        # delete the local target branch (clone stays clean)
```
On conflict: capture unmerged files (`git diff --name-only --diff-filter=U`), run
`git merge --abort`, return `IsConflict = true` with the file list. **Nothing is pushed.**
Either way the clone is returned to `Git.DefaultBranch` (default `master`; empty ⇒ stay
on target) **and the local target branch is deleted** as best-effort final steps that never
fail the merge — so the clone keeps only remote branches. (If `DefaultBranch` is empty these
steps are skipped, since you can't delete the branch you're on.) Note: with `push == false`
the merge lived only in that local branch, so deleting it discards the un-pushed result.

> Because of the `checkout -B` reset, the app must point at a **dedicated working clone**
> it owns — never a repo edited by hand. `git` must have **non-interactive credentials**
> (SSH agent / cached HTTPS token / PAT) so background fetch/clone/push work without prompts.

---

## Architecture

### Backend (`backend/`, .NET 8, ASP.NET Core Web API)
Dependencies: **Cronos** (cron parsing) and **Velopack** (installer + in-app
auto-update). In production the API also **serves the built Vue app** as static files
(single-server mode) on `http://localhost:5080`.

**Program.cs** — `VelopackApp.Build().Run()` **as the very first line** (handles
install/update/uninstall hooks; a no-op under `dotnet run`), then DI registration, CORS
(dev), static file serving + SPA fallback, applies the run-on-startup preference
(`WindowsStartup.Apply`), and auto-opens the browser on start when not in Development —
**unless** launched with `--startup` (the login autostart passes it so boot is quiet).

**Windowless / autostart:** Release/published builds are **`OutputType=WinExe`** (set in the
csproj) so the installed app runs in the **background with no console window**; Debug
(`dotnet run`) stays a console app for dev logs. `AppSettings.RunOnStartup` (default true,
toggle in Settings) registers an `HKCU\...\Run` entry to the current exe on every launch
(`WindowsStartup`), so it **starts on Windows login and self-heals across updates**. Both
are Windows-only and only act for an installed (Velopack) build.

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
  `GetBranchesAsync`, `MergeAsync`, `GetRepoStatusAsync`, `EnsureRepositoryAsync` (clone),
  `IsBusy` + `AcquireExclusiveAsync` (wait for git idle / hold the lock — used by the
  self-updater so an update never interrupts a merge). `GetBranchesAsync` returns **remote
  branches only** (local branches in the clone are transient merge byproducts and are
  deleted after each merge — see below), so the dropdown never shows a branch twice.
- `BranchCache` + `BranchFetchBackgroundService` — the fetcher polls `git fetch` on the
  configured interval and refreshes the cache; controllers read the cache (fast).
- `ScheduleStore` — schedules persisted to `schedules.json`.
- `SchedulerBackgroundService` — ticks every 15s; runs due schedules ordered by
  `NextRunUtc` then `Order`; notifies on conflict/failure. Cron next-occurrence via Cronos.
- `NotificationStore` — capped in-app feed persisted to `notifications.json`.
- `NotificationChannels` (`INotificationChannel` + `InAppChannel`) — **in-app only**
  (webhook/email were removed). `NotificationService` fans out (currently just in-app).
- `UpdateService` — dual-mode update check, cached ~6h. When the app was installed via
  the **Velopack** installer it uses `UpdateManager` + `GithubSource` to detect, download
  and apply updates in place (`CanSelfUpdate = true`, the "Update now" button). When not
  a Velopack install (dev / old portable build) it falls back to a plain GitHub
  `releases/latest` tag comparison so the banner still shows, but self-update is off.
  `DownloadAndRestartAsync` downloads, then (in the background) **waits for any in-flight
  git op to finish** via `IGitService.AcquireExclusiveAsync` before `ApplyUpdatesAndRestart`
  — so a merge/clone/fetch is never interrupted (10-min timeout, then applies anyway). Posts
  in-app notifications ("Update waiting" / "Updating") while it waits. `ApplyUpdatesAndRestart`
  is a hard process exit, so all background workers stop with it; the frontend polls until the
  server drops and returns, then reloads. On each fresh check it also raises an "Update
  available" notification **once per new version** (`MaybeNotifyAvailableAsync`).
- `UpdateCheckBackgroundService` — re-checks GitHub **hourly** (`GetAsync(force:true)`) so a
  long-running instance surfaces the banner/notification without a page reload. The frontend
  also re-checks hourly. First check ~20s after startup.

**Controllers/** (`api/...`)
- `BranchesController` — `GET /api/branches` (cache), `POST /api/branches/refresh`.
- `MergeController` — `POST /api/merge` (instant); notifies on conflict.
- `SchedulesController` — CRUD + `PUT /api/schedules/reorder` + `POST /{id}/toggle`.
- `NotificationsController` — feed, read-all, clear, `POST /api/notifications/test`.
- `SettingsController` — `GET/PUT /api/settings`, `GET /api/settings/repo-status`, `POST /api/settings/clone`.
- `UpdateController` — `GET /api/update`, `POST /api/update/check`,
  `POST /api/update/apply` (download + apply + restart; Velopack installs only).

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
  banners (repo-not-ready; **update-available** with an **"Update now"** button when
  `canSelfUpdate`, else a Download link). Clicking Update now opens a **blocking modal
  dialog** (`.update-overlay`) that shows live phase text (`updateStatus`, read from the
  backend's "Update" notifications: downloading → waiting for a running merge → restarting),
  then `watchForRestart()` polls until the server drops and returns and reloads. Header
  (bell + gear).
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

**Production — portable (one folder, single-server):**
```
./build.sh            # or .\build.ps1   (framework-dependent; needs .NET 8 runtime)
./build.sh osx-arm64  # or a RID for a self-contained single-file build
```
Produces `output/` (published API + `wwwroot/` with the built UI) and a launcher
(`Start.bat` / `Start.command` / `start.sh`). Serves UI + API on `:5080` and opens the
browser. **`build.sh` deletes `output/` first — that's safe now because data lives in the
per-user directory, not `output/`.** The portable build shows the update banner but
**cannot self-update** (it's not a Velopack install) — use it for dev/testing, not for
the shipped installer.

**Production — installer + auto-update (Windows, Velopack):**
```
dotnet tool install -g vpk         # one time
.\pack.ps1                          # build + pack into .\releases (no upload)
.\pack.ps1 -Upload -Token <PAT>     # ...also create + upload the GitHub release
```
`pack.ps1` reads `<Version>` from the csproj (single source of truth — bump it per
release), builds the frontend, publishes the backend **self-contained win-x64**
(NOT single-file — Velopack repackages), copies the UI into `wwwroot/`, then runs
`vpk pack` → `releases/` with `Setup.exe` + `*-full.nupkg` + `releases.win.json`.
Velopack installs **per-user** to `%LocalAppData%\BranchMerger` (no admin), so the
in-app **"Update now"** button downloads + applies + restarts without a prompt. See
"Release / update flow" below.

Config: **everything is edited in the ⚙️ Settings screen at runtime** (persisted to
`settings.json`); `appsettings.json` only seeds first-run defaults.

### Release / update flow (Velopack)
1. Bump `<Version>` in `backend/BranchMerger.Api.csproj` (e.g. `1.0.0` → `1.0.1`).
2. `.\pack.ps1 -Upload -Token <PAT>` — publishes GitHub release `v<Version>`.
   The **git tag must match `<Version>`** (`v1.0.1` ↔ `1.0.1`), and **every** file in
   `releases/` must be uploaded (the `releases.win.json` manifest is what installed apps
   read). Manual alternative: create the release in the GitHub UI and drag in all of
   `releases/`.
3. Installed copies poll `UpdateCheck.GitHubRepo`, see the newer version, and show
   "Update now" → one click updates in place.
4. **User data is never touched** — the three JSON files live in
   `%APPDATA%\BranchMerger`, separate from the `%LocalAppData%\BranchMerger` program dir.
5. The **first** install must come from a Velopack `Setup.exe` (not the portable folder),
   otherwise self-update stays off.

---

## Current default config (`backend/appsettings.json`)
- `Git.RepositoryUrl` = "" (set per machine in the Settings screen)
- `Git.RemoteName` = `origin`
- `Git.FetchIntervalSeconds` = 60
- `Git.RepositoryPath` = "" (must be set per machine — the dedicated clone)
- `Git.DefaultBranch` = `master` (checked out after every merge so the clone rests there;
  empty ⇒ leave it on the target branch)
- `DataDirectory` = "" (empty ⇒ per-user default)
- `UpdateCheck.GitHubRepo` = `eweleylee/branch-merger` (the update source; set to
  `owner/repo` — required for the banner and Velopack self-update)
- `UpdateCheck.Prerelease` = `false` (set `true` to also offer pre-release tags)
- `UpdateCheck.CurrentVersion` = "" — **leave empty.** Version priority is: this override
  → the installed (Velopack) version → assembly version (`<Version>` in the csproj = 1.0.0).
  Only set it to force a version for testing the banner.

## Conventions & gotchas
- **Dedicated clone only** (merge resets local target). **Non-interactive git creds required.**
- All git ops are **serialized**; there is no per-repo concurrency yet.
- Cron is **5-field, interpreted in the server PC's local timezone** (Cronos with
  `TimeZoneInfo.Local` in `ComputeNextRun`; `NextRunUtc` is still stored/compared as UTC).
  The UI's `nextRun`/`formatNext` matches in **local** time to mirror this (browser runs
  on the same PC). `nextRun` handles numeric/list/range/step fields but not name-based days
  (`MON`) or `L`/`#`; cronstrue still describes those in words.
- Persistent state = three JSON files in the per-user data dir. **No database.**
- Notifications are **in-app only** (webhook/email intentionally removed).
- The TFS remote URL needs a **PAT or cached Windows auth** for non-interactive push/fetch.
- When editing shared components, **update the demo copy too** (or re-copy).
- **Updates:** the version lives in `<Version>` in the csproj (single source of truth);
  bump it before every release. Ship via `pack.ps1` (Velopack), and **upload all files in
  `releases/`** — the `releases.win.json` manifest is required. Self-update only works for
  apps installed from a Velopack `Setup.exe`.

---

## Environment note for Claude Code
Unlike the chat this was built in, Claude Code can actually **compile the backend**
(`dotnet build`) and run everything — please do, since the C# was written but not
compiled in-chat (no .NET SDK there). Verify with `dotnet build` in `backend/` and
`npm run build` in `frontend/` after changes.

---

## Open work (discussed, not yet built) — rough priority
1. **Windows Service support** — `UseWindowsService()` + `install-service.ps1` /
   `uninstall-service.ps1`. **Largely superseded:** "hide the console" and "start on Windows
   startup" are now done via `OutputType=WinExe` + the `HKCU\...\Run` entry (`WindowsStartup`),
   which also stays compatible with the Velopack auto-updater (a service does not). A real
   service would only add: running with no user logged in, and it must run under an account
   that has the git credentials (LocalSystem won't). Consider **file logging** separately now
   that Release builds are windowless.
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
8. **CI release workflow** — a GitHub Actions job that runs `vpk pack` + `vpk upload
   github` on a version tag, so publishing a release is fully automated (currently
   `pack.ps1` is run by hand).
9. Minor: hide number-input spinners.

**Done since:** in-app auto-update — one-click "Update now" (download + apply + restart)
via **Velopack**, plus the `pack.ps1` installer/release pipeline. See "Build & run".
