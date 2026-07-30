import { Injectable } from '@angular/core';

// Error that carries the parsed response body (so callers can read .data.conflictedFiles etc.).
export class ApiError extends Error {
  data: any;
  constructor(message: string, data: any) { super(message); this.data = data; }
}

function parseSSE(raw: string): { event: string; data: string } {
  let event = 'message';
  const dataLines: string[] = [];
  for (const line of raw.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim();
    else if (line.startsWith('data:')) dataLines.push(line.slice(5).replace(/^ /, ''));
  }
  return { event, data: dataLines.join('\n') };
}

/**
 * Thin wrapper around the backend API. All calls go through /api which the dev
 * proxy forwards to the C# server (same origin in production).
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private async req(url: string, options: RequestInit = {}): Promise<any> {
    const res = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...options });
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) {
      const message = data?.message || data?.Message || `Request failed (${res.status})`;
      throw new ApiError(message, data);
    }
    return data;
  }

  getBranches() { return this.req('/api/branches'); }
  refreshBranches() { return this.req('/api/branches/refresh', { method: 'POST' }); }

  /** Streaming refresh: onStep(line) for each git-fetch line, then onResult({branches,lastUpdatedUtc}). */
  async refreshStream(onStep: (line: string) => void, onResult: (r: any) => void): Promise<void> {
    const res = await fetch('/api/branches/refresh/stream', { method: 'POST' });
    if (!res.ok || !res.body) throw new Error(`Refresh failed (${res.status})`);

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';
    let gotResult = false;

    for (; ;) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      let sep: number;
      while ((sep = buf.indexOf('\n\n')) >= 0) {
        const evt = parseSSE(buf.slice(0, sep));
        buf = buf.slice(sep + 2);
        if (evt.event === 'step') onStep(evt.data);
        else if (evt.event === 'result') { gotResult = true; onResult(JSON.parse(evt.data)); }
      }
    }
    if (!gotResult) throw new Error('The refresh ended without a result.');
  }

  merge(payload: any) { return this.req('/api/merge', { method: 'POST', body: JSON.stringify(payload) }); }

  /** Streaming merge: onStep(line) per git step, then onResult(mergeResult). */
  async mergeStream(payload: any, onStep: (line: string) => void, onResult: (r: any) => void): Promise<void> {
    const res = await fetch('/api/merge/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok || !res.body) throw new Error(`Merge failed (${res.status})`);

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';
    let gotResult = false;

    for (; ;) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      let sep: number;
      while ((sep = buf.indexOf('\n\n')) >= 0) {
        const evt = parseSSE(buf.slice(0, sep));
        buf = buf.slice(sep + 2);
        if (evt.event === 'step') onStep(evt.data);
        else if (evt.event === 'result') { gotResult = true; onResult(JSON.parse(evt.data)); }
      }
    }
    if (!gotResult) throw new Error('The merge ended without a result.');
  }

  getSchedules() { return this.req('/api/schedules'); }
  createSchedule(payload: any) { return this.req('/api/schedules', { method: 'POST', body: JSON.stringify(payload) }); }
  toggleSchedule(id: string) { return this.req(`/api/schedules/${id}/toggle`, { method: 'POST' }); }
  deleteSchedule(id: string) { return this.req(`/api/schedules/${id}`, { method: 'DELETE' }); }
  reorderSchedules(orderedIds: string[]) { return this.req('/api/schedules/reorder', { method: 'PUT', body: JSON.stringify(orderedIds) }); }

  getNotifications() { return this.req('/api/notifications'); }
  markAllRead() { return this.req('/api/notifications/read-all', { method: 'POST' }); }
  markRead(id: string) { return this.req(`/api/notifications/${id}/read`, { method: 'POST' }); }
  clearNotifications() { return this.req('/api/notifications/clear', { method: 'POST' }); }
  testNotification() { return this.req('/api/notifications/test', { method: 'POST' }); }

  getSettings() { return this.req('/api/settings'); }
  saveSettings(settings: any) { return this.req('/api/settings', { method: 'PUT', body: JSON.stringify(settings) }); }
  getRepoStatus() { return this.req('/api/settings/repo-status'); }
  cloneRepo() { return this.req('/api/settings/clone', { method: 'POST' }); }

  getUpdate() { return this.req('/api/update'); }
  applyUpdate() { return this.req('/api/update/apply', { method: 'POST' }); }

  getLogs() { return this.req('/api/logs'); }
  getLog(name: string) { return this.req('/api/logs/' + encodeURIComponent(name)); }
}
