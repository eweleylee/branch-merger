<script setup>
import { ref, watch } from 'vue'
import { api } from '../api.js'

const props = defineProps({
  schedules: { type: Array, default: () => [] }
})
const emit = defineEmits(['changed'])

// Local, mutable copy so drag reordering feels instant; re-synced when props change.
const groups = ref([])
const dragCtx = ref(null)   // { groupKey, index }
const togglingId = ref(null)
const deletingId = ref(null)

function timeKeyOf(s) {
  return s.type === 'Once' ? `once:${s.runAtUtc}` : `cron:${s.cronExpression}`
}
function timeLabelOf(s) {
  if (s.type === 'Once') return `Once · ${s.runAtUtc ? new Date(s.runAtUtc).toLocaleString() : '—'}`
  return `Recurring · ${s.cronExpression} (local time)`
}
function nextRunOf(s) {
  return s.nextRunUtc ? new Date(s.nextRunUtc).getTime() : Number.MAX_SAFE_INTEGER
}

function buildGroups(list) {
  const map = new Map()
  for (const s of list) {
    const key = timeKeyOf(s)
    if (!map.has(key)) map.set(key, { key, label: timeLabelOf(s), sortAt: nextRunOf(s), items: [] })
    map.get(key).items.push({ ...s })
  }
  const arr = [...map.values()]
  arr.forEach(g => g.items.sort((a, b) => (a.order - b.order)))
  arr.sort((a, b) => a.sortAt - b.sortAt)          // soonest time group first
  return arr
}

watch(() => props.schedules, (list) => { groups.value = buildGroups(list) }, { immediate: true, deep: true })

// --- drag within a group only ---
function onDragStart(groupKey, index) { dragCtx.value = { groupKey, index } }

function onDrop(groupKey, index) {
  const ctx = dragCtx.value
  dragCtx.value = null
  if (!ctx || ctx.groupKey !== groupKey || ctx.index === index) return   // ignore cross-group / no-op

  const group = groups.value.find(g => g.key === groupKey)
  const items = group.items
  const [moved] = items.splice(ctx.index, 1)
  items.splice(index, 0, moved)

  persistOrder()
}

async function persistOrder() {
  // Flatten every group in display order → global ordered id list.
  const ids = groups.value.flatMap(g => g.items.map(i => i.id))
  await api.reorderSchedules(ids)
  emit('changed')
}

async function toggle(id) {
  togglingId.value = id
  try { await api.toggleSchedule(id); emit('changed') }
  finally { togglingId.value = null }
}
async function remove(id) {
  deletingId.value = id
  try { await api.deleteSchedule(id); emit('changed') }
  finally { deletingId.value = null }
}

function fmt(dt) { return dt ? new Date(dt).toLocaleString() : '—' }
function statusClass(st) { return !st ? '' : (st.startsWith('Success') ? 'ok' : 'err') }
</script>

<template>
  <section class="card">
    <h2>Scheduled merges</h2>

    <p v-if="!schedules.length" class="empty">
      No schedules yet. Create one from the panel on the left.
    </p>

    <template v-else>
      <p class="hint">
        Drag rows to reorder. Ordering only affects merges that run at the <strong>same time</strong>
        (shown grouped below); the top one in a group runs first.
      </p>

      <div class="sched-scroll">
      <div v-for="group in groups" :key="group.key" class="group">
        <div class="group-head">
          <span>{{ group.label }}</span>
          <span v-if="group.items.length > 1" class="tag">runs in this order ↓</span>
          <span v-else class="tag muted">only one — order n/a</span>
        </div>

        <div
          v-for="(s, i) in group.items"
          :key="s.id"
          class="row"
          :class="{ off: !s.enabled, orderable: group.items.length > 1 }"
          :draggable="group.items.length > 1"
          @dragstart="onDragStart(group.key, i)"
          @dragover.prevent
          @drop="onDrop(group.key, i)">

          <span class="grip" v-if="group.items.length > 1" title="Drag to reorder">⠿</span>
          <span class="seq" v-if="group.items.length > 1">{{ i + 1 }}</span>

          <span class="branches">
            <span class="src">{{ s.sourceBranch }}</span>
            <span class="into">→</span>
            <span class="tgt">{{ s.targetBranch }}</span>
            <span v-if="s.push" class="pill">push</span>
          </span>

          <span class="status" :class="statusClass(s.lastStatus)">
            {{ s.lastStatus || 'not run yet' }}
          </span>

          <span class="actions">
            <button class="mini" :disabled="togglingId===s.id" @click="toggle(s.id)">
              <span v-if="togglingId===s.id" class="spinner"></span>{{ s.enabled ? 'Pause' : 'Resume' }}
            </button>
            <button class="mini danger" :disabled="deletingId===s.id" @click="remove(s.id)">
              <span v-if="deletingId===s.id" class="spinner"></span>Delete
            </button>
          </span>
        </div>
      </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.card { background: var(--panel); border: 1px solid var(--border); border-radius: 12px; padding: 22px; min-width: 0; }
h2 { margin: 0 0 12px; font-size: 18px; }
.empty { color: var(--muted); font-size: 14px; }
.hint { color: var(--muted); font-size: 12px; margin: 0 0 16px; }

.sched-scroll { overflow-x: auto; }
.group { margin-bottom: 16px; min-width: 440px; }
.group-head {
  display: flex; align-items: center; gap: 10px;
  font-size: 12px; color: var(--muted); padding: 6px 4px; border-bottom: 1px solid var(--border);
}
.tag { margin-left: auto; font-size: 11px; }
.tag.muted { opacity: .7; }

.row {
  display: grid; grid-template-columns: auto auto 1fr auto auto; align-items: center; gap: 10px;
  padding: 10px 8px; border-bottom: 1px solid var(--border); font-size: 13px;
}
.row.orderable { cursor: grab; }
.row.orderable:active { cursor: grabbing; }
.row.off { opacity: .55; }
.grip { color: var(--muted); font-size: 14px; letter-spacing: -2px; }
.seq {
  width: 20px; height: 20px; border-radius: 50%; display: grid; place-items: center;
  background: var(--panel-2); color: var(--muted); font-size: 11px;
}
.branches { min-width: 0; }
.src { color: var(--accent); }
.tgt { color: var(--accent-2); }
.into { color: var(--muted); margin: 0 6px; }
.pill { margin-left: 8px; font-size: 11px; background: var(--panel-2); padding: 1px 7px; border-radius: 6px; color: var(--muted); }

.status { font-size: 12px; color: var(--muted); white-space: nowrap; }
.status.ok { color: var(--accent-2); }
.status.err { color: var(--danger); }

.actions { display: flex; gap: 8px; white-space: nowrap; }
.mini { background: transparent; border: 1px solid var(--border); color: var(--text); border-radius: 8px; padding: 5px 10px; font-size: 12px; cursor: pointer; }
.mini.danger { border-color: var(--danger); color: var(--danger); }
</style>
