<script setup>
import { ref, onMounted } from 'vue'
import { api } from '../api.js'

const emit = defineEmits(['close'])

const files = ref([])
const selected = ref('')
const entries = ref([])
const total = ref(0)
const truncated = ref(false)
const loading = ref(false)
const error = ref(null)

async function loadFiles() {
  loading.value = true; error.value = null
  try {
    const data = await api.getLogs()
    files.value = data.files || []
    if (files.value.length) {
      selected.value = files.value[0].name   // newest first
      await loadEntries()
    } else {
      entries.value = []
    }
  } catch (e) { error.value = e.message } finally { loading.value = false }
}

async function loadEntries() {
  if (!selected.value) return
  loading.value = true; error.value = null
  try {
    const data = await api.getLog(selected.value)
    entries.value = data.entries || []
    total.value = data.total || 0
    truncated.value = !!data.truncated
  } catch (e) { error.value = e.message } finally { loading.value = false }
}

function levelClass(level) {
  if (level === 'Error' || level === 'Critical') return 'err'
  if (level === 'Warning') return 'warn'
  return 'info'
}
function fmtSize(n) {
  if (n < 1024) return n + ' B'
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1024 / 1024).toFixed(1) + ' MB'
}
function fileLabel(f) { return `${f.date}  (${fmtSize(f.sizeBytes)})` }

onMounted(loadFiles)
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <div class="modal">
      <div class="modal-head">
        <h2>📄 Logs</h2>
        <div class="head-controls">
          <select v-if="files.length" v-model="selected" @change="loadEntries">
            <option v-for="f in files" :key="f.name" :value="f.name">{{ fileLabel(f) }}</option>
          </select>
          <button class="btn-ghost small" :disabled="loading" @click="selected ? loadEntries() : loadFiles()">
            <span v-if="loading" class="spinner"></span>↻
          </button>
          <button class="btn-ghost" @click="emit('close')">✕</button>
        </div>
      </div>

      <div class="body">
        <p class="hint">Newest first. Errors and merge conflicts are recorded here; files older than 30 days are removed automatically.</p>

        <div v-if="error" class="msg err">{{ error }}</div>
        <div v-else-if="!files.length && !loading" class="empty">No logs yet — nothing has been recorded.</div>
        <div v-else-if="!entries.length && !loading" class="empty">This log is empty.</div>

        <div v-else class="entries">
          <div v-for="(e, i) in entries" :key="i" class="entry">
            <div class="meta">
              <span class="time">{{ e.time }}</span>
              <span class="lvl" :class="levelClass(e.level)">{{ e.level }}</span>
            </div>
            <pre class="text">{{ e.text }}</pre>
          </div>
          <p v-if="truncated" class="hint">Showing the {{ entries.length }} most recent of {{ total }} entries.</p>
        </div>
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
  width: 100%; max-width: 860px; max-height: 90vh; display: flex; flex-direction: column;
  background: var(--panel); border: 1px solid var(--border); border-radius: 14px;
  box-shadow: 0 24px 64px rgba(0,0,0,.5);
}
.modal-head {
  display: flex; align-items: center; gap: 12px; padding: 16px 20px; border-bottom: 1px solid var(--border);
}
.modal-head h2 { margin: 0; font-size: 18px; flex: 1; }
.head-controls { display: flex; align-items: center; gap: 8px; }
.head-controls select { width: auto; min-width: 180px; }
.small { padding: 6px 10px; font-size: 13px; }

.body { padding: 12px 20px 16px; overflow: auto; }
.hint { color: var(--muted); font-size: 12px; margin: 0 0 12px; }
.empty { color: var(--muted); font-size: 14px; padding: 24px 0; text-align: center; }

.entries { display: flex; flex-direction: column; gap: 8px; }
.entry { border: 1px solid var(--border); border-radius: 8px; padding: 8px 10px; background: var(--panel-2); }
.meta { display: flex; align-items: center; gap: 10px; margin-bottom: 4px; }
.time { color: var(--muted); font-size: 12px; font-variant-numeric: tabular-nums; }
.lvl { font-size: 11px; font-weight: 700; padding: 1px 8px; border-radius: 6px; }
.lvl.err { color: #fff; background: var(--danger); }
.lvl.warn { color: #06231a; background: var(--warn); }
.lvl.info { color: var(--muted); background: var(--panel); border: 1px solid var(--border); }
.text { margin: 0; white-space: pre-wrap; word-break: break-word; font-size: 12.5px; color: var(--text); }

.msg { margin: 4px 0; padding: 10px 12px; border-radius: 8px; font-size: 13px; }
.msg.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
</style>
