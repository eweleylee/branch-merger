// Thin wrapper around the backend API. All calls go through /api which Vite
// proxies to the C# server during development.

// Parse one SSE record ("event:"/"data:" lines) into { event, data }.
function parseSSE(raw) {
  let event = 'message'
  const dataLines = []
  for (const line of raw.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim()
    else if (line.startsWith('data:')) dataLines.push(line.slice(5).replace(/^ /, ''))
  }
  return { event, data: dataLines.join('\n') }
}

async function req(url, options = {}) {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options
  })
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) {
    const message = data?.message || data?.Message || `Request failed (${res.status})`
    const err = new Error(message)
    err.data = data   // preserve full body (e.g. MergeResult with conflictedFiles + log)
    throw err
  }
  return data
}

export const api = {
  getBranches: () => req('/api/branches'),
  refreshBranches: () => req('/api/branches/refresh', { method: 'POST' }),

  merge: (payload) => req('/api/merge', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),

  // Streaming merge: calls onStep(line) for each git step as it runs, then
  // onResult(mergeResult) at the end. Reads the SSE response body incrementally.
  mergeStream: async (payload, onStep, onResult) => {
    const res = await fetch('/api/merge/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    })
    if (!res.ok || !res.body) throw new Error(`Merge failed (${res.status})`)

    const reader = res.body.getReader()
    const decoder = new TextDecoder()
    let buf = ''
    let gotResult = false

    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      buf += decoder.decode(value, { stream: true })
      let sep
      while ((sep = buf.indexOf('\n\n')) >= 0) {
        const evt = parseSSE(buf.slice(0, sep))
        buf = buf.slice(sep + 2)
        if (evt.event === 'step') onStep(evt.data)
        else if (evt.event === 'result') { gotResult = true; onResult(JSON.parse(evt.data)) }
      }
    }
    if (!gotResult) throw new Error('The merge ended without a result.')
  },

  getSchedules: () => req('/api/schedules'),
  createSchedule: (payload) => req('/api/schedules', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  toggleSchedule: (id) => req(`/api/schedules/${id}/toggle`, { method: 'POST' }),
  deleteSchedule: (id) => req(`/api/schedules/${id}`, { method: 'DELETE' }),
  reorderSchedules: (orderedIds) => req('/api/schedules/reorder', {
    method: 'PUT',
    body: JSON.stringify(orderedIds)
  }),

  getNotifications: () => req('/api/notifications'),
  markAllRead: () => req('/api/notifications/read-all', { method: 'POST' }),
  markRead: (id) => req(`/api/notifications/${id}/read`, { method: 'POST' }),
  clearNotifications: () => req('/api/notifications/clear', { method: 'POST' }),
  testNotification: () => req('/api/notifications/test', { method: 'POST' }),

  getSettings: () => req('/api/settings'),
  saveSettings: (settings) => req('/api/settings', {
    method: 'PUT',
    body: JSON.stringify(settings)
  }),
  getRepoStatus: () => req('/api/settings/repo-status'),
  cloneRepo: () => req('/api/settings/clone', { method: 'POST' }),

  getUpdate: () => req('/api/update'),
  applyUpdate: () => req('/api/update/apply', { method: 'POST' }),

  getLogs: () => req('/api/logs'),
  getLog: (name) => req('/api/logs/' + encodeURIComponent(name))
}
