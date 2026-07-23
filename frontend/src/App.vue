<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { api } from './api.js'
import MergePanel from './components/MergePanel.vue'
import ScheduleList from './components/ScheduleList.vue'
import NotificationBell from './components/NotificationBell.vue'
import SettingsPanel from './components/SettingsPanel.vue'

const branches = ref([])
const branchesUpdatedAt = ref(null)
const branchError = ref(null)
const schedules = ref([])
const notifications = ref([])
const unread = ref(0)
const showSettings = ref(false)
const repoStatus = ref(null)
const updateInfo = ref(null)
const updateDismissed = ref(false)
const updating = ref(false)
const updateError = ref(null)

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
  try {
    await api.applyUpdate()
    // The server downloads the update then restarts itself. Give it a moment to
    // come back up on the new version, then reload this page.
    setTimeout(() => window.location.reload(), 5000)
  } catch (e) {
    updateError.value = e.message
    updating.value = false
  }
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
  try {
    const data = await api.refreshBranches()
    branches.value = data.branches || []
    branchesUpdatedAt.value = data.lastUpdatedUtc
  } catch (e) {
    branchError.value = e.message
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

    <div v-if="updateInfo && updateInfo.updateAvailable && !updateDismissed" class="update-banner">
      <template v-if="updating">
        <span><strong>Updating to {{ updateInfo.latestVersion }}…</strong> the app will restart and this page will reload automatically.</span>
      </template>
      <template v-else>
        <span><strong>Update available</strong> — {{ updateInfo.latestVersion }} (you have {{ updateInfo.currentVersion }}).</span>
        <button v-if="updateInfo.canSelfUpdate" class="update-now" @click="applyUpdate">Update now</button>
        <a v-else-if="updateInfo.url" :href="updateInfo.url" target="_blank" rel="noopener" class="link">Download</a>
        <span v-if="updateError" class="update-err">{{ updateError }}</span>
        <button class="dismiss" @click="updateDismissed = true" aria-label="Dismiss">✕</button>
      </template>
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
