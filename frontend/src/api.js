// Thin wrapper around the backend API. All calls go through /api which Vite
// proxies to the C# server during development.

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
  applyUpdate: () => req('/api/update/apply', { method: 'POST' })
}
