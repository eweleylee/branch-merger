# 🌿 Branch Merger

A small full-stack tool for merging one Git branch into another — instantly or on a
schedule. C# / ASP.NET Core backend, Vue 3 (Vite) frontend.

## What it does
- Pick a **source** branch (merge FROM) and a **target** branch (merge INTO), e.g. `master` → `feature/x`.
- **Merge now** with one click, or set up a schedule:
  - **Once** — run at a specific date/time.
  - **Recurring** — a cron expression (UTC).
- The backend **fetches branches continuously in the background** so the dropdowns always show the latest branches.
- **Same-time ordering:** when several schedules fire at the same time they run one after another (never in parallel); drag rows in the schedule list to set which goes first. Ordering only matters within a shared time — different times are decided by the clock.
- Schedules run **server-side**, so they fire even if the browser tab is closed (as long as the backend is running).
- Optionally **push** the target branch back to the remote after a successful merge.
- **Notifies you on merge conflicts** (and on any failed scheduled run) via an in-app feed (the bell).

## Notifications
Merge conflicts (and failed scheduled runs) raise an **in-app alert** shown in the 🔔 bell:
each entry lists the branches, the trigger (manual vs scheduled), and the conflicted files.
The feed persists to `notifications.json`. There are no email/webhook channels.


## How the merge works
For a merge of `source → target`, the backend runs:
```
git fetch <remote> --prune
git checkout -B <target> <remote>/<target>   # reset local target to match remote
git merge --no-edit <remote>/<source>
git push <remote> <target>                   # only if "push" is checked
```
If the merge hits conflicts it is **aborted automatically** and nothing is pushed.

> ⚠️ Point the app at a **dedicated working clone**, not a repo you edit by hand.
> The merge resets the local target branch to match the remote.

## Configuration — all in the app, no file editing

Everything (repository path, repository URL, remote, fetch interval, and all
notification settings) is edited from the **⚙️ Settings** screen in the UI and saved
to `backend/settings.json` at runtime. You don't edit backend code or config to change them.

- `appsettings.json` only **seeds** `settings.json` on first run; after that, `settings.json` is the source of truth.
- `settings.json` holds secrets (e.g. SMTP password) and is gitignored. The API never sends the saved password back to the browser.

### Point it at a repo (from the Settings screen)
1. Set **Working clone path** to a dedicated folder this app owns.
2. Either clone it yourself there, or set a **Repository URL** and click **Clone / initialize** — the app runs `git clone` for you.
3. **Check status** confirms the clone is a valid git working tree and shows the current branch.

> ⚠️ Use a **dedicated working clone**, not a repo you edit by hand — merges reset the local target branch to match the remote.

## Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- `git` on your PATH, with credentials configured for **non-interactive** use
  (SSH key in an agent, or a cached HTTPS token) so background fetch/clone/push work without prompts.

## Run in development (two servers, hot reload)

**Backend** (terminal 1):
```bash
cd backend
dotnet run
# API on http://localhost:5080
```

**Frontend** (terminal 2):
```bash
cd frontend
npm install
npm run dev
# UI on http://localhost:5173
```

Open http://localhost:5173. The Vite dev server proxies `/api` to the backend.

## Production build (one folder, click to start)

The build script compiles the Vue app, publishes the API, and drops everything into
`./output/`. In production the **API serves the built UI itself**, so it's a single
process on a single URL — no separate frontend server, no CORS.

**macOS / Linux:**
```bash
./build.sh                # framework-dependent (needs the .NET 8 runtime installed)
./build.sh osx-arm64      # OR self-contained single file (no runtime needed)
```

**Windows (PowerShell):**
```powershell
.\build.ps1               # framework-dependent
.\build.ps1 win-x64       # OR self-contained single .exe
```

Then start it by double-clicking the launcher in `output/`:
- **Windows:** `Start.bat`
- **macOS:** `Start.command`
- **Linux:** `start.sh`

The app prints its address, **opens your browser automatically** to
http://localhost:5080, and keeps running in that window. Close the window to stop it.

Common runtime identifiers for a self-contained build: `win-x64`, `osx-arm64`,
`osx-x64`, `linux-x64`. Self-contained needs no .NET install but is larger and
platform-specific; framework-dependent is small but needs the .NET 8 runtime.

> macOS may block the first launch (unsigned): right-click `Start.command` → Open, or
> allow it under System Settings → Privacy & Security. On Linux you may need
> `chmod +x start.sh BranchMerger.Api` if the executable bit was lost in transit.

The `output/` folder is self-contained — you can zip it and copy it to another
machine (matching the runtime, or self-contained). Its `settings.json`,
`schedules.json` and `notifications.json` are created there at runtime.


## Where your data lives (survives rebuilds)
Settings, schedules, and notifications are stored in a stable **per-user data directory**,
separate from the program files — so rebuilding or replacing the app never wipes them:
- Windows: `%APPDATA%\\BranchMerger`
- macOS / Linux: `~/.config/BranchMerger`

Override with `DataDirectory` in `appsettings.json` if you want a custom path. The app
prints the resolved location on startup. (Older builds that stored data next to the
executable are migrated automatically on first run.)

## Checking for updates (GitHub Releases)
The app can tell you when a newer version is published, via GitHub Releases. Set your repo
in `appsettings.json`:
```json
"UpdateCheck": { "Enabled": true, "GitHubRepo": "eweleylee/branch-merger", "CurrentVersion": "" }
```
- `GitHubRepo` — your repository, i.e. `eweleylee/branch-merger`. Leave blank to turn the check off.
- `CurrentVersion` — leave blank to use the assembly version (`<Version>` in the csproj); set it to override.

On startup the app queries `releases/latest`, compares the tag to its own version, and shows
an in-app **"Update available"** banner when a newer release exists. If the app was installed
via the Velopack installer, the banner shows a one-click **"Update now"** button that downloads
and applies the update, then restarts; otherwise it shows a Download link to the release.
To publish an update: bump `<Version>` in the csproj, run `release.bat` (or `pack.ps1` /
`release.sh`), and cut a matching GitHub Release tagged `vX.Y.Z` with the files from `releases/`.
Results are cached (~6h) to stay under GitHub's rate limit.

## API
| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/branches` | Cached branch list + last-updated time |
| POST | `/api/branches/refresh` | Force an immediate fetch |
| POST | `/api/merge` | Merge now `{ sourceBranch, targetBranch, push }` |
| GET | `/api/schedules` | List schedules |
| POST | `/api/schedules` | Create `{ sourceBranch, targetBranch, push, type, runAtUtc?, cronExpression? }` |
| POST | `/api/schedules/{id}/toggle` | Pause / resume |
| DELETE | `/api/schedules/{id}` | Delete |
| GET | `/api/notifications` | Alert feed + unread count |
| POST | `/api/notifications/read-all` | Mark all read |
| POST | `/api/notifications/clear` | Clear feed |
| POST | `/api/notifications/test` | Send a test alert |
| GET | `/api/settings` | Current settings (password masked) |
| PUT | `/api/settings` | Save settings |
| GET | `/api/settings/repo-status` | Is the working clone ready? |
| POST | `/api/settings/clone` | Clone from the configured URL |

Schedules are persisted to `backend/schedules.json`.

## Notes & next steps
- Cron expressions use standard 5-field syntax in **UTC**.
- Persistence is a JSON file; swap `ScheduleStore` for EF Core + a database for production.
- There's no auth — run it locally or behind your own gateway.
