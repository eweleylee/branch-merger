<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'

const emit = defineEmits(['close', 'saved'])

const loading = ref(true)
const saving = ref(false)
const cloning = ref(false)
const checking = ref(false)
const message = ref(null)          // { ok, text }
const repoStatus = ref(null)

// mirrors backend AppSettings
const s = ref({
  git: { repositoryPath: '', repositoryUrl: '', remoteName: 'origin', fetchIntervalSeconds: 60, defaultBranch: 'master' },
  runOnStartup: true
})

async function load() {
  loading.value = true
  try {
    const data = await api.getSettings()
    s.value = data.settings
    await checkRepo()
  } catch (e) {
    message.value = { ok: false, text: e.message }
  } finally {
    loading.value = false
  }
}

async function checkRepo() {
  checking.value = true
  try { repoStatus.value = await api.getRepoStatus() }
  catch (e) { repoStatus.value = { ready: false, message: e.message } }
  finally { checking.value = false }
}

async function save() {
  saving.value = true; message.value = null
  try {
    const data = await api.saveSettings(s.value)
    s.value = data.settings
    message.value = { ok: true, text: 'Settings saved.' }
    await checkRepo()
    emit('saved')
  } catch (e) {
    message.value = { ok: false, text: e.message }
  } finally {
    saving.value = false
  }
}

async function cloneRepo() {
  cloning.value = true; message.value = null
  try {
    // Save first so the backend clones using the values on screen.
    await api.saveSettings(s.value)
    repoStatus.value = await api.cloneRepo()
    message.value = { ok: repoStatus.value.ready, text: repoStatus.value.message }
    emit('saved')
  } catch (e) {
    message.value = { ok: false, text: e.data?.message || e.message }
    if (e.data) repoStatus.value = e.data
  } finally {
    cloning.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <div class="modal">
      <div class="modal-head">
        <h2>⚙️ Settings</h2>
        <button class="btn-ghost" @click="emit('close')">✕</button>
      </div>

      <div v-if="loading" class="loading">Loading…</div>

      <div v-else class="body">
        <!-- Repository -->
        <section>
          <h3>Repository</h3>
          <div class="field">
            <label>Working clone path (a dedicated folder this app owns)</label>
            <input type="text" v-model="s.git.repositoryPath" placeholder="C:\\merger\\repo-clone" />
          </div>
          <div class="field">
            <label>Repository URL (used to clone if the path is empty)</label>
            <input type="text" v-model="s.git.repositoryUrl" placeholder="git@github.com:org/repo.git" />
          </div>
          <div class="two">
            <div class="field">
              <label>Remote name</label>
              <input type="text" v-model="s.git.remoteName" placeholder="origin" />
            </div>
            <div class="field">
              <label>Fetch interval (seconds)</label>
              <input type="number" min="10" v-model.number="s.git.fetchIntervalSeconds" />
            </div>
          </div>
          <div class="field">
            <label>Default branch (checked out after each merge · empty = stay on target)</label>
            <input type="text" v-model="s.git.defaultBranch" placeholder="master" />
          </div>

          <div class="repo-status" v-if="repoStatus">
            <span :class="['dot', repoStatus.ready ? 'on' : 'off']"></span>
            {{ repoStatus.message }}
            <span v-if="repoStatus.currentBranch" class="muted"> · on {{ repoStatus.currentBranch }}</span>
          </div>
          <div class="row-btns">
            <button class="btn-ghost small" :disabled="checking" @click="checkRepo">
              <span v-if="checking" class="spinner"></span>{{ checking ? 'Checking…' : 'Check status' }}
            </button>
            <button class="btn-primary small" :disabled="cloning" @click="cloneRepo">
              <span v-if="cloning" class="spinner"></span>{{ cloning ? 'Cloning…' : 'Clone / initialize' }}
            </button>
          </div>
        </section>

        <!-- Startup -->
        <section>
          <h3>Startup</h3>
          <label class="check">
            <input type="checkbox" v-model="s.runOnStartup" />
            Start automatically when Windows starts
          </label>
          <p class="hint">Runs in the background (no console window). Applies to the installed app only.</p>
        </section>

        <div v-if="message" :class="['msg', message.ok ? 'ok' : 'err']">{{ message.text }}</div>
      </div>

      <div class="modal-foot">
        <div class="spacer"></div>
        <button class="btn-ghost" @click="emit('close')">Close</button>
        <button class="btn-primary" :disabled="saving" @click="save">
          <span v-if="saving" class="spinner"></span>{{ saving ? 'Saving…' : 'Save settings' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed; inset: 0; background: rgba(0,0,0,.55);
  display: grid; place-items: center; z-index: 50; padding: 20px;
}
.modal {
  width: 100%; max-width: 640px; max-height: 90vh; display: flex; flex-direction: column;
  background: var(--panel); border: 1px solid var(--border); border-radius: 14px;
  box-shadow: 0 24px 64px rgba(0,0,0,.5);
}
.modal-head, .modal-foot {
  display: flex; align-items: center; gap: 10px; padding: 16px 20px;
}
.modal-head { border-bottom: 1px solid var(--border); }
.modal-foot { border-top: 1px solid var(--border); }
.modal-head h2 { margin: 0; font-size: 18px; flex: 1; }
.spacer { flex: 1; }
.loading { padding: 40px; text-align: center; color: var(--muted); }

.body { padding: 8px 20px 4px; overflow: auto; }
section { padding: 14px 0; border-bottom: 1px solid var(--border); }
section:last-of-type { border-bottom: none; }
h3 { margin: 0 0 12px; font-size: 14px; color: var(--accent); }

.field { margin-bottom: 12px; }
.two { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.check { display: flex; align-items: center; gap: 8px; color: var(--text); font-size: 14px; margin: 4px 0 12px; }
.check input { width: auto; }

.repo-status { font-size: 13px; margin: 6px 0 10px; display: flex; align-items: center; gap: 8px; }
.dot { width: 9px; height: 9px; border-radius: 50%; display: inline-block; }
.dot.on { background: var(--accent-2); }
.dot.off { background: var(--warn); }
.muted { color: var(--muted); }
.row-btns { display: flex; gap: 8px; }
.small { padding: 6px 12px; font-size: 12px; }

.hint { font-size: 12px; color: var(--muted); margin: 8px 0 0; }
.msg { margin: 14px 0 4px; padding: 10px 12px; border-radius: 8px; font-size: 13px; }
.msg.ok { background: rgba(63,178,127,.12); border: 1px solid var(--accent-2); }
.msg.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
</style>
