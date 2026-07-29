// Cron helpers for the live "Runs …" echo under the recurring field.
// Ported from the Vue app. cronstrue handles essentially any valid expression;
// a hand-rolled fallback covers the common cases when cronstrue can't parse.
import cronstrue from 'cronstrue';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const two = (n: number) => ('0' + n).slice(-2);

export interface CronInfo { ok: boolean; text: string; source?: string; }

export function handRolled(expr: string): CronInfo {
  const p = (expr || '').trim().split(/\s+/);
  if (p.length !== 5) return { ok: false, text: 'Enter 5 fields: minute hour day month weekday.' };
  const [m, h, dom, , dow] = p;
  if (m === '*' && h === '*') return { ok: true, text: 'Runs every minute' };
  const perMin = m.match(/^\*\/(\d+)$/);
  if (perMin && h === '*') return { ok: true, text: `Runs every ${perMin[1]} minutes` };
  if (/^\d+$/.test(m) && h === '*') return { ok: true, text: `Runs every hour at :${two(+m)}` };
  const perHour = h.match(/^\*\/(\d+)$/);
  if (/^\d+$/.test(m) && perHour) return { ok: true, text: `Runs every ${perHour[1]} hours at :${two(+m)}` };
  if (/^\d+$/.test(m) && /^\d+$/.test(h)) {
    const t = `${two(+h)}:${two(+m)}`;
    if (dom === '*' && dow === '*') return { ok: true, text: `Runs every day at ${t}` };
    if (dow !== '*' && dom === '*') return { ok: true, text: `Runs every ${dow.split(',').map(x => DAYS[+x] || x).join(', ')} at ${t}` };
    if (dom !== '*' && dow === '*') return { ok: true, text: `Runs on day ${dom} of the month at ${t}` };
  }
  return { ok: true, text: 'Runs on a custom schedule' };
}

export function detailed(expr: string): CronInfo {
  try { return { ok: true, text: cronstrue.toString((expr || '').trim(), { verbose: false }) }; }
  catch (e: any) { return { ok: false, text: 'Could not parse: ' + (e && e.message ? e.message : e) }; }
}

export function describeCron(expr: string, engine: 'auto' | 'cronstrue' | 'simple' = 'auto'): CronInfo {
  if (engine === 'simple') return { ...handRolled(expr), source: 'built-in' };
  if (engine === 'cronstrue') return { ...detailed(expr), source: 'cronstrue' };
  const d = detailed(expr);
  if (d.ok) return { ...d, source: 'cronstrue' };
  return { ...handRolled(expr), source: 'built-in' };
}

function matchField(f: string, v: number, mn: number, mx: number): boolean {
  if (f === '*') return true;
  return f.split(',').some(part => {
    let step = 1, r = part;
    if (part.includes('/')) { const a = part.split('/'); r = a[0]; step = parseInt(a[1], 10) || 1; }
    let lo: number, hi: number;
    if (r === '*') { lo = mn; hi = mx; }
    else if (r.includes('-')) { const b = r.split('-'); lo = +b[0]; hi = +b[1]; }
    else { lo = hi = +r; }
    if (isNaN(lo) || isNaN(hi)) return false;
    if (v < lo || v > hi) return false;
    if (step === 1) return true;
    return (v - (r === '*' ? mn : lo)) % step === 0;
  });
}

export function cronMatches(expr: string, d: Date): boolean {
  const p = (expr || '').trim().split(/\s+/);
  if (p.length !== 5) return false;
  // Match in local time to mirror the backend (Cronos with TimeZoneInfo.Local).
  return matchField(p[0], d.getMinutes(), 0, 59) &&
    matchField(p[1], d.getHours(), 0, 23) &&
    matchField(p[2], d.getDate(), 1, 31) &&
    matchField(p[3], d.getMonth() + 1, 1, 12) &&
    matchField(p[4], d.getDay(), 0, 6);
}

export function nextRun(expr: string): Date | null {
  const d = new Date(); d.setSeconds(0, 0);
  for (let i = 0; i < 60 * 24 * 8; i++) {
    d.setMinutes(d.getMinutes() + 1);
    if (cronMatches(expr, d)) return new Date(d);
  }
  return null;
}

export function formatNext(d: Date | null): string {
  if (!d) return '';
  const now = new Date(), tm = new Date(now); tm.setDate(now.getDate() + 1);
  const day = d.toDateString() === now.toDateString() ? 'Today'
    : d.toDateString() === tm.toDateString() ? 'Tomorrow'
      : d.toLocaleDateString();
  const loc = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  return `next run ${day} ${loc} (local time)`;
}
