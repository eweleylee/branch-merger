<script setup>
import { ref, computed } from 'vue'
import { api } from '../api.js'

const props = defineProps({
  items: { type: Array, default: () => [] },
  unread: { type: Number, default: 0 }
})
const emit = defineEmits(['changed'])

const open = ref(false)

const sorted = computed(() => props.items)

function close() {
  open.value = false
}

async function toggleOpen() {
  open.value = !open.value
  if (open.value && props.unread > 0) {
    await api.markAllRead()
    emit('changed')
  }
}
async function clearAll() {
  await api.clearNotifications()
  emit('changed')
}
async function sendTest() {
  await api.testNotification()
  emit('changed')
}

function icon(level) {
  return level === 'Error' ? '🔴' : level === 'Warning' ? '⚠️' : 'ℹ️'
}
function fmt(dt) {
  return new Date(dt).toLocaleString()
}
</script>

<template>
  <div class="bell-wrap">
    <button class="bell" @click="toggleOpen" :aria-label="`${unread} unread notifications`">
      🔔
      <span v-if="unread > 0" class="badge">{{ unread > 99 ? '99+' : unread }}</span>
    </button>

    <div v-if="open" class="backdrop" @click="close"></div>

    <div v-if="open" class="panel">
      <div class="panel-head">
        <strong>Notifications</strong>
        <div class="panel-actions">
          <button class="link" @click="sendTest">Test</button>
          <button class="link" @click="clearAll" :disabled="!items.length">Clear</button>
          <button class="link close" @click="close" aria-label="Close notifications">✕</button>
        </div>
      </div>

      <p v-if="!items.length" class="empty">Nothing yet. Conflict and failure alerts will show up here.</p>

      <ul v-else>
        <li v-for="n in sorted" :key="n.id" :class="['item', n.level.toLowerCase()]">
          <div class="row1">
            <span class="ico">{{ icon(n.level) }}</span>
            <span class="title">{{ n.title }}</span>
            <span class="time">{{ fmt(n.createdUtc) }}</span>
          </div>
          <div v-if="n.sourceBranch && n.targetBranch" class="branches">
            <span class="src">{{ n.sourceBranch }}</span> →
            <span class="tgt">{{ n.targetBranch }}</span>
            <span v-if="n.trigger" class="trigger">· {{ n.trigger }}</span>
          </div>
          <div class="msg">{{ n.message }}</div>
          <ul v-if="n.conflictedFiles && n.conflictedFiles.length" class="files">
            <li v-for="f in n.conflictedFiles" :key="f"><code>{{ f }}</code></li>
          </ul>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.bell-wrap { position: relative; }
.bell {
  position: relative; font-size: 18px; line-height: 1; padding: 0;
  width: 42px; height: 42px; border-radius: 50%;
  display: grid; place-items: center; background: var(--panel-2);
}
.badge {
  position: absolute; top: -4px; right: -4px;
  background: var(--danger); color: #fff; font-size: 11px; font-weight: 700;
  min-width: 18px; height: 18px; padding: 0 5px; border-radius: 9px;
  display: grid; place-items: center;
}

.panel {
  position: absolute; right: 0; top: 50px; width: 360px; max-width: calc(100vw - 24px);
  max-height: 460px; overflow: auto;
  background: var(--panel); border: 1px solid var(--border); border-radius: 12px;
  box-shadow: 0 16px 48px rgba(0,0,0,.45); z-index: 20; padding: 8px;
}
@media (max-width: 640px) {
  .panel { position: fixed; top: 64px; left: 12px; right: 12px; width: auto; max-width: none; max-height: 70vh; }
}
.panel-head { display: flex; justify-content: space-between; align-items: center; padding: 8px 10px; }
.panel-actions { display: flex; gap: 6px; align-items: center; }
.link { background: transparent; border: none; color: var(--accent); padding: 4px 6px; font-size: 12px; cursor: pointer; }
.link:disabled { color: var(--muted); }
.link.close { color: var(--muted); font-size: 14px; }
.backdrop { position: fixed; inset: 0; z-index: 19; background: transparent; }
.empty { color: var(--muted); font-size: 13px; padding: 14px 10px; }

ul { list-style: none; margin: 0; padding: 0; }
.item { padding: 10px; border-radius: 8px; border: 1px solid transparent; }
.item + .item { margin-top: 4px; }
.item.warning { background: rgba(224,160,58,.08); border-color: rgba(224,160,58,.3); }
.item.error   { background: rgba(229,88,77,.08);  border-color: rgba(229,88,77,.3); }
.item.info    { background: var(--panel-2); }

.row1 { display: flex; align-items: center; gap: 8px; }
.title { font-weight: 600; font-size: 14px; flex: 1; }
.time { font-size: 11px; color: var(--muted); }
.branches { font-size: 12px; margin: 4px 0 2px 26px; }
.src { color: var(--accent); }
.tgt { color: var(--accent-2); }
.trigger { color: var(--muted); }
.msg { font-size: 13px; color: var(--text); margin-left: 26px; }
.files { margin: 6px 0 0 26px; }
.files li { font-size: 12px; }
.files code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }
</style>
