<script setup>
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'

const props = defineProps({
  modelValue: { type: String, default: '' },     // the selected branch .name (e.g. "origin/feature/x")
  branches: { type: Array, default: () => [] },   // BranchInfo[]
  placeholder: { type: String, default: 'Search branches…' }
})
const emit = defineEmits(['update:modelValue'])

const root = ref(null)
const inputEl = ref(null)
const listId = 'bl-' + Math.random().toString(36).slice(2, 8)

const open = ref(false)
const query = ref('')
const activeIndex = ref(-1)

// Match on the short name (ignoring the remote prefix), which is how people think.
const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  const list = [...props.branches].sort((a, b) => (b.isRemote - a.isRemote))
  if (!q) return list
  return list.filter(b => b.shortName.toLowerCase().includes(q) || b.name.toLowerCase().includes(q))
})

const selected = computed(() => props.branches.find(b => b.name === props.modelValue) || null)
const displayLabel = computed(() => selected.value ? selected.value.shortName : '')

// When open, the field shows what you're typing; when closed, the chosen branch.
const shownValue = computed(() => open.value ? query.value : displayLabel.value)

function openList() {
  if (open.value) return
  open.value = true
  query.value = ''
  activeIndex.value = filtered.value.findIndex(b => b.name === props.modelValue)
}
function close() {
  open.value = false
  query.value = ''
  activeIndex.value = -1
}
function choose(b) {
  emit('update:modelValue', b.name)
  close()
  inputEl.value && inputEl.value.blur()
}

function onInput(e) {
  if (!open.value) open.value = true
  query.value = e.target.value
  activeIndex.value = filtered.value.length ? 0 : -1
}
function move(delta) {
  if (!open.value) { openList(); return }
  const n = filtered.value.length
  if (!n) return
  activeIndex.value = (activeIndex.value + delta + n) % n
  scrollActiveIntoView()
}
function enter() {
  if (open.value && activeIndex.value >= 0 && filtered.value[activeIndex.value]) {
    choose(filtered.value[activeIndex.value])
  }
}
async function scrollActiveIntoView() {
  await nextTick()
  const el = root.value?.querySelector('.opt.active')
  el && el.scrollIntoView({ block: 'nearest' })
}

function onDocMouseDown(e) {
  if (root.value && !root.value.contains(e.target)) close()
}
onMounted(() => document.addEventListener('mousedown', onDocMouseDown))
onUnmounted(() => document.removeEventListener('mousedown', onDocMouseDown))

// If the selected branch disappears from the list (pruned), clear the selection.
watch(() => props.branches, (list) => {
  if (props.modelValue && !list.some(b => b.name === props.modelValue)) emit('update:modelValue', '')
})
</script>

<template>
  <div class="branch-select" ref="root">
    <div class="control" :class="{ open }">
      <input
        ref="inputEl"
        type="text"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="open"
        :aria-controls="listId"
        :aria-activedescendant="activeIndex >= 0 ? listId + '-' + activeIndex : undefined"
        :value="shownValue"
        :placeholder="selected ? selected.shortName : placeholder"
        @focus="openList"
        @click="openList"
        @input="onInput"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.enter.prevent="enter"
        @keydown.esc.prevent="close" />
      <span class="chev" aria-hidden="true" @mousedown.prevent="open ? close() : (inputEl && inputEl.focus())">▾</span>
    </div>

    <ul v-if="open" class="list" :id="listId" role="listbox">
      <li v-if="!filtered.length" class="empty">No matching branches</li>
      <li
        v-for="(b, i) in filtered"
        :key="b.name"
        :id="listId + '-' + i"
        class="opt"
        :class="{ active: i === activeIndex, chosen: b.name === modelValue }"
        role="option"
        :aria-selected="b.name === modelValue"
        @mouseenter="activeIndex = i"
        @mousedown.prevent="choose(b)">
        <span class="nm">{{ b.shortName }}</span>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.branch-select { position: relative; }
.control { position: relative; }
.control input {
  width: 100%; padding: 9px 30px 9px 11px;
  color: var(--text); background: var(--panel); border: 1px solid var(--border);
  border-radius: 8px; outline: none; font: inherit;
}
.control input:focus { border-color: var(--accent); }
.control.open input { border-color: var(--accent); }
.chev { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); color: var(--muted); cursor: pointer; font-size: 12px; }

.list {
  position: absolute; z-index: 25; left: 0; right: 0; top: calc(100% + 4px);
  margin: 0; padding: 4px; list-style: none;
  max-height: 240px; overflow-y: auto;
  background: var(--panel); border: 1px solid var(--border); border-radius: 8px;
  box-shadow: 0 12px 36px rgba(0,0,0,.45);
}
.opt {
  display: flex; align-items: center; gap: 8px; justify-content: space-between;
  padding: 8px 10px; border-radius: 6px; cursor: pointer; font-size: 13px;
}
.opt.active { background: var(--panel-2); }
.opt.chosen .nm { color: var(--accent); }
.nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.empty { padding: 10px; color: var(--muted); font-size: 13px; }
</style>
