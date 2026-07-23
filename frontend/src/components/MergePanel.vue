<script setup>
import { ref, computed } from 'vue'
import { api } from '../api.js'
import { describeCron, nextRun, formatNext } from '../cron.js'
import BranchSelect from './BranchSelect.vue'

const props = defineProps({
  branches: { type: Array, default: () => [] },
  branchesUpdatedAt: { type: String, default: null }
})
const emit = defineEmits(['refresh', 'scheduled'])

const source = ref('')          // merge FROM
const target = ref('')          // merge INTO
const push = ref(true)

// mode: 'now' | 'once' | 'cron'
const mode = ref('now')
const runAtLocal = ref('')      // datetime-local value (browser local time)
const cron = ref('0 2 * * *')   // default: every day at 02:00 UTC

// Live plain-English translation + next run (cronstrue, with built-in fallback).
const cronInfo = computed(() => describeCron(cron.value))
const cronNext = computed(() => cronInfo.value.ok ? formatNext(nextRun(cron.value)) : '')

const busy = ref(false)
const result = ref(null)        // { ok, message, log }

// Show branches with commit info, remote branches first (they are the freshest).
const branchOptions = computed(() =>
  [...props.branches].sort((a, b) => (b.isRemote - a.isRemote))
)

const canSubmit = computed(() =>
  source.value && target.value && source.value !== target.value && !busy.value
)

async function mergeNow() {
  busy.value = true; result.value = null
  try {
    const r = await api.merge({
      sourceBranch: source.value,
      targetBranch: target.value,
      push: push.value
    })
    result.value = { ok: true, message: r.message, log: r.log }
  } catch (e) {
    result.value = {
      ok: false,
      message: e.message,
      log: e.data?.log,
      conflictedFiles: e.data?.conflictedFiles || []
    }
  } finally {
    busy.value = false
  }
}

async function createSchedule() {
  busy.value = true; result.value = null
  try {
    const payload = {
      sourceBranch: source.value,
      targetBranch: target.value,
      push: push.value,
      type: mode.value === 'once' ? 'Once' : 'Cron'
    }
    if (mode.value === 'once') {
      if (!runAtLocal.value) throw new Error('Pick a date and time.')
      // datetime-local is local time; convert to UTC ISO for the backend.
      payload.runAtUtc = new Date(runAtLocal.value).toISOString()
    } else {
      payload.cronExpression = cron.value.trim()
    }
    await api.createSchedule(payload)
    result.value = { ok: true, message: 'Schedule created.' }
    emit('scheduled')
  } catch (e) {
    result.value = { ok: false, message: e.message }
  } finally {
    busy.value = false
  }
}

function submit() {
  if (mode.value === 'now') mergeNow()
  else createSchedule()
}
</script>

<template>
  <section class="card">
    <div class="head">
      <h2>Merge branches</h2>
      <div class="updated">
        <span v-if="branchesUpdatedAt">branches updated {{ new Date(branchesUpdatedAt).toLocaleTimeString() }}</span>
        <button class="btn-ghost small" @click="emit('refresh')">↻ Refresh</button>
      </div>
    </div>

    <div class="merge-row">
      <div class="field">
        <label>Merge from (source)</label>
        <BranchSelect v-model="source" :branches="branchOptions" placeholder="Search branches…" />
      </div>

      <div class="arrow">→</div>

      <div class="field">
        <label>Merge into (target)</label>
        <BranchSelect v-model="target" :branches="branchOptions" placeholder="Search branches…" />
      </div>
    </div>

    <label class="check">
      <input type="checkbox" v-model="push" />
      Push target to remote after a successful merge
    </label>

    <!-- Scheduling options -->
    <div class="modes">
      <button :class="['seg', mode==='now'  && 'seg-on']" @click="mode='now'">Merge now</button>
      <button :class="['seg', mode==='once' && 'seg-on']" @click="mode='once'">Schedule once</button>
      <button :class="['seg', mode==='cron' && 'seg-on']" @click="mode='cron'">Recurring</button>
    </div>

    <div v-if="mode==='once'" class="field">
      <label>Run at (your local time)</label>
      <input type="datetime-local" v-model="runAtLocal" />
    </div>

    <div v-else-if="mode==='cron'" class="field">
      <label>Cron expression (UTC · minute hour day month weekday)</label>
      <input type="text" v-model="cron" placeholder="0 2 * * *" />

      <div class="cron-echo" :class="{ bad: !cronInfo.ok }">
        <span class="ic" aria-hidden="true">{{ cronInfo.ok ? '✓' : '⚠' }}</span>
        <span>{{ cronInfo.text }}</span>
      </div>
      <div v-if="cronNext" class="cron-next">{{ cronNext }}</div>

      <p class="hint">
        Examples: <code>*/30 * * * *</code> every 30 min ·
        <code>0 2 * * *</code> daily 02:00 ·
        <code>0 8 * * 1</code> Mondays 08:00
      </p>
    </div>

    <div class="actions">
      <button
        :class="mode==='now' ? 'btn-success' : 'btn-primary'"
        :disabled="!canSubmit"
        @click="submit">
        <span v-if="busy">Working…</span>
        <span v-else-if="mode==='now'">Merge now</span>
        <span v-else>Create schedule</span>
      </button>
    </div>

    <div v-if="result" :class="['result', result.ok ? 'ok' : 'err']">
      <strong>{{ result.ok ? 'Success' : 'Problem' }}:</strong> {{ result.message }}
      <ul v-if="result.conflictedFiles && result.conflictedFiles.length" class="conflicts">
        <li v-for="f in result.conflictedFiles" :key="f"><code>{{ f }}</code></li>
      </ul>
      <pre v-if="result.log">{{ result.log }}</pre>
    </div>
  </section>
</template>

<style scoped>
.card {
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 22px;
}
.head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 18px; }
h2 { margin: 0; font-size: 18px; }
.updated { display: flex; gap: 12px; align-items: center; font-size: 12px; color: var(--muted); }
.small { padding: 5px 10px; font-size: 12px; }

.merge-row { display: grid; grid-template-columns: 1fr auto 1fr; gap: 14px; align-items: end; }
.arrow { font-size: 22px; color: var(--accent); padding-bottom: 8px; }
.field { margin-bottom: 4px; }

.check { display: flex; gap: 8px; align-items: center; color: var(--text); margin: 16px 0; font-size: 14px; }
.check input { width: auto; }

.modes { display: flex; gap: 8px; margin: 6px 0 16px; }
.seg { flex: 1; background: var(--panel-2); }
.seg-on { border-color: var(--accent); color: #fff; box-shadow: inset 0 0 0 1px var(--accent); }

.hint { font-size: 12px; color: var(--muted); margin: 8px 0 0; }
.hint code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }

.cron-echo { display: flex; align-items: center; gap: 8px; margin-top: 10px; font-size: 13px; color: var(--accent-2); }
.cron-echo.bad { color: var(--danger); }
.cron-echo .ic { font-weight: 700; }
.cron-next { margin-top: 4px; font-size: 12px; color: var(--muted); }

.actions { margin-top: 18px; }
.actions button { min-width: 160px; }

.result { margin-top: 16px; padding: 12px 14px; border-radius: 8px; font-size: 14px; }
.result.ok { background: rgba(63,178,127,.12); border: 1px solid var(--accent-2); }
.result.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
.result pre {
  margin: 10px 0 0; white-space: pre-wrap; font-size: 12px; color: var(--muted);
  max-height: 220px; overflow: auto;
}
.conflicts { margin: 8px 0 0; padding-left: 18px; }
.conflicts li { font-size: 13px; margin: 2px 0; }
.conflicts code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }
</style>
