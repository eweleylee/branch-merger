<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from './api.js'
import MergePanel from './components/MergePanel.vue'
import ScheduleList from './components/ScheduleList.vue'
import NotificationBell from './components/NotificationBell.vue'
import SettingsPanel from './components/SettingsPanel.vue'

const branches = ref([])
const branchesUpdatedAt = ref(null)
const branchError = ref(null)
const refreshing = ref(false)
const schedules = ref([])
const notifications = ref([])
const unread = ref(0)
const showSettings = ref(false)
const repoStatus = ref(null)
const updateInfo = ref(null)
const updateDismissed = ref(false)
const updating = ref(false)
const updateError = ref(null)
const updateStartedAt = ref(null)
const canReload = ref(false)

// Live phase text for the update dialog, taken from the backend's "Update" notifications
// (emitted as it downloads / waits for a running merge / restarts).
const updateStatus = computed(() => {
  if (!updateStartedAt.value) return ''
  const n = notifications.value.find(
    x => x.trigger === 'Update' && new Date(x.createdUtc) >= updateStartedAt.value
  )
  return n?.message || 'Downloading the update…'
})

let pollTimer = null

async function loadRepoStatus() {
  try { repoStatus.value = await api.getRepoStatus() }
  catch { /* ignore */ }
}

async function loadUpdate() {
  try { updateInfo.value = await api.getUpdate() }
  catch { /* ignore */ }
}

async function applyUpdate() {
  updating.value = true
  updateError.value = null
  canReload.value = false
  updateStartedAt.value = new Date(Date.now() - 3000)   // small buffer for clock/poll skew
  try {
    await api.applyUpdate()
    // The server downloads, waits for any in-flight git op, then restarts. That can
    // take a while, so poll until it goes down (restarting) and comes back, then reload.
    watchForRestart()
  } catch (e) {
    updateError.value = e.message
    updating.value = false
  }
}

function reloadNow() { window.location.reload() }

function watchForRestart() {
  let sawDown = false
  let waited = 0
  const step = 2000
  const cap = 10 * 60 * 1000   // give up after 10 min (matches server idle-wait timeout)
  const iv = setInterval(async () => {
    waited += step
    try {
      await api.getUpdate()
      if (sawDown) { clearInterval(iv); window.location.reload() }   // back up on the new version
    } catch {
      sawDown = true   // server unreachable = restarting
    }
    // Refresh notifications so the dialog's live status ("waiting"/"installing") stays current.
    loadNotifications()
    if (waited >= cap) {
      clearInterval(iv)
      updateError.value = 'Update is taking longer than expected. Reload the page once the app has restarted.'
      canReload.value = true   // keep the dialog open with a manual reload option
    }
  }, step)
}

async function loadBranches() {
  try {
    const data = await api.getBranches()
    branches.value = data.branches || []
    branchesUpdatedAt.value = data.lastUpdatedUtc
    branchError.value = data.lastError
  } catch (e) {
    branchError.value = e.message
  }
}

async function forceRefresh() {
  refreshing.value = true
  try {
    const data = await api.refreshBranches()
    branches.value = data.branches || []
    branchesUpdatedAt.value = data.lastUpdatedUtc
  } catch (e) {
    branchError.value = e.message
  } finally {
    refreshing.value = false
  }
}

async function loadSchedules() {
  try { schedules.value = await api.getSchedules() }
  catch { /* ignore transient errors */ }
}

async function loadNotifications() {
  try {
    const data = await api.getNotifications()
    notifications.value = data.items || []
    unread.value = data.unread || 0
  } catch { /* ignore transient errors */ }
}

onMounted(() => {
  loadBranches()
  loadSchedules()
  loadNotifications()
  loadRepoStatus()
  loadUpdate()
  // Poll so dropdowns, schedule statuses and alerts stay fresh with the background workers.
  pollTimer = setInterval(() => {
    loadBranches()
    loadSchedules()
    loadNotifications()
  }, 10000)
})

function onSettingsSaved() {
  loadRepoStatus()
  forceRefresh()
}

onUnmounted(() => clearInterval(pollTimer))
</script>

<template>
  <div class="wrap">
    <header>
      <div class="title-block">
        <h1>🌿 Branch Merger</h1>
        <p class="sub">Choose a source and target branch, merge instantly, or schedule it.</p>
      </div>
      <div class="header-actions">
        <NotificationBell
          :items="notifications"
          :unread="unread"
          @changed="loadNotifications" />
        <button class="gear" @click="showSettings = true" aria-label="Settings">⚙️</button>
      </div>
    </header>

    <div v-if="updateInfo && updateInfo.updateAvailable && !updateDismissed && !updating" class="update-banner">
      <span><strong>Update available</strong> — {{ updateInfo.latestVersion }} (you have {{ updateInfo.currentVersion }}).</span>
      <button v-if="updateInfo.canSelfUpdate" class="update-now" @click="applyUpdate">Update now</button>
      <a v-else-if="updateInfo.url" :href="updateInfo.url" target="_blank" rel="noopener" class="link">Download</a>
      <span v-if="updateError" class="update-err">{{ updateError }}</span>
      <button class="dismiss" @click="updateDismissed = true" aria-label="Dismiss">✕</button>
    </div>

    <!-- Update-in-progress dialog: a blocking overlay so it can't be missed. -->
    <div v-if="updating" class="update-overlay">
      <div class="update-modal" role="dialog" aria-modal="true" aria-live="polite">
        <div v-if="!updateError" class="spinner"></div>
        <h2>{{ updateError ? 'Update interrupted' : 'Updating Branch Merger' }}</h2>
        <p v-if="updateInfo" class="ver">Version {{ updateInfo.latestVersion }}</p>
        <p v-if="!updateError" class="status">{{ updateStatus }}</p>
        <p v-if="!updateError" class="sub">The app will restart and this page reloads automatically. Please don’t close this window.</p>
        <p v-if="updateError" class="err">{{ updateError }}</p>
        <button v-if="canReload" class="btn-primary reload-btn" @click="reloadNow">Reload now</button>
      </div>
    </div>

    <div v-if="repoStatus && !repoStatus.ready" class="banner">
      Repository not ready: {{ repoStatus.message }}
      <button class="link" @click="showSettings = true">Open Settings</button>
    </div>

    <div v-else-if="branchError" class="banner">
      Could not read branches: {{ branchError }}
      <button class="link" @click="showSettings = true">Open Settings</button>
    </div>

    <div class="grid">
      <MergePanel
        :branches="branches"
        :branches-updated-at="branchesUpdatedAt"
        :refreshing="refreshing"
        @refresh="forceRefresh"
        @scheduled="loadSchedules" />

      <ScheduleList
        :schedules="schedules"
        @changed="loadSchedules" />
    </div>

    <footer>
      Backend fetches branches continuously in the background · schedules run server-side even if this tab is closed.
    </footer>

    <SettingsPanel
      v-if="showSettings"
      @close="showSettings = false"
      @saved="onSettingsSaved" />
  </div>
</template>

<style scoped>
.wrap { max-width: 1100px; margin: 0 auto; padding: 40px 24px 60px; }
header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
h1 { margin: 0; font-size: 26px; letter-spacing: -.3px; }
.sub { margin: 6px 0 0; color: var(--muted); }
.header-actions { display: flex; align-items: center; gap: 10px; }
.gear {
  font-size: 18px; width: 42px; height: 42px; border-radius: 50%; padding: 0;
  display: grid; place-items: center; background: var(--panel-2);
}
.banner .link {
  background: transparent; border: none; color: var(--accent);
  padding: 0 0 0 8px; font: inherit; cursor: pointer; text-decoration: underline;
}
.update-banner {
  display: flex; align-items: center; gap: 12px; margin-bottom: 20px;
  background: rgba(79,140,255,.12); border: 1px solid var(--accent);
  color: #cfe0ff; padding: 10px 14px; border-radius: 8px; font-size: 14px;
}
.update-banner .link { color: var(--accent); text-decoration: underline; }
.update-banner .update-now {
  background: var(--accent); border: none; color: #fff;
  padding: 5px 12px; border-radius: 6px; font: inherit; font-weight: 600;
  cursor: pointer;
}
.update-banner .update-now:hover { filter: brightness(1.08); }
.update-banner .update-err { color: var(--danger); font-size: 13px; }
.update-banner .dismiss {
  margin-left: auto; background: transparent; border: none; color: var(--muted);
  cursor: pointer; font-size: 14px; padding: 2px 6px;
}

/* Update-in-progress dialog */
.update-overlay {
  position: fixed; inset: 0; z-index: 100;
  background: rgba(0,0,0,.6); backdrop-filter: blur(2px);
  display: grid; place-items: center; padding: 20px;
}
.update-modal {
  background: var(--panel); border: 1px solid var(--accent); border-radius: 14px;
  padding: 30px 34px; max-width: 440px; text-align: center;
  box-shadow: 0 24px 64px rgba(0,0,0,.5);
}
.update-modal .spinner {
  width: 38px; height: 38px; border-width: 3px; margin: 0 auto 18px;
  color: var(--accent); display: block;
}
.update-modal h2 { margin: 0 0 6px; font-size: 20px; }
.update-modal .ver { margin: 0 0 14px; color: var(--muted); font-size: 13px; }
.update-modal .status { margin: 0 0 10px; color: var(--text); font-size: 15px; font-weight: 600; }
.update-modal .sub { margin: 0; color: var(--muted); font-size: 13px; line-height: 1.5; }
.update-modal .err { margin: 8px 0 0; color: var(--danger); font-size: 14px; }
.update-modal .reload-btn { margin-top: 18px; }

.banner {
  background: rgba(224,160,58,.12);
  border: 1px solid var(--warn);
  color: #f0d3a0;
  padding: 12px 14px; border-radius: 8px; margin-bottom: 20px; font-size: 14px;
}
.banner .hint { display: block; margin-top: 4px; color: var(--muted); font-size: 12px; }
.banner code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }

.grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 20px; align-items: start; }
@media (max-width: 900px) { .grid { grid-template-columns: minmax(0, 1fr); } }

footer { margin-top: 28px; color: var(--muted); font-size: 12px; text-align: center; }
</style>
