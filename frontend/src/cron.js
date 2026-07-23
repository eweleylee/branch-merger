// Cron helpers for the live "Runs …" echo under the recurring field.
//
// Two translators are supported and both ship in the app:
//   - cronstrue  : handles essentially any valid expression (ranges, steps, names, L/#)
//   - built-in   : zero-dependency, covers the common patterns, vague otherwise
// The engine selector picks between them; 'auto' prefers cronstrue and falls back
// to the built-in parser if cronstrue can't parse the input.
import cronstrue from 'cronstrue'

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const two = n => ('0' + n).slice(-2)

// --- built-in (hand-rolled) translator -------------------------------------
export function handRolled(expr) {
  const p = (expr || '').trim().split(/\s+/)
  if (p.length !== 5) return { ok: false, text: 'Enter 5 fields: minute hour day month weekday.' }
  const [m, h, dom, , dow] = p
  if (m === '*' && h === '*') return { ok: true, text: 'Runs every minute' }
  const perMin = m.match(/^\*\/(\d+)$/)
  if (perMin && h === '*') return { ok: true, text: `Runs every ${perMin[1]} minutes` }
  if (/^\d+$/.test(m) && h === '*') return { ok: true, text: `Runs every hour at :${two(+m)}` }
  const perHour = h.match(/^\*\/(\d+)$/)
  if (/^\d+$/.test(m) && perHour) return { ok: true, text: `Runs every ${perHour[1]} hours at :${two(+m)}` }
  if (/^\d+$/.test(m) && /^\d+$/.test(h)) {
    const t = `${two(+h)}:${two(+m)} UTC`
    if (dom === '*' && dow === '*') return { ok: true, text: `Runs every day at ${t}` }
    if (dow !== '*' && dom === '*') return { ok: true, text: `Runs every ${dow.split(',').map(x => DAYS[+x] || x).join(', ')} at ${t}` }
    if (dom !== '*' && dow === '*') return { ok: true, text: `Runs on day ${dom} of the month at ${t}` }
  }
  return { ok: true, text: 'Runs on a custom schedule' }
}

// --- cronstrue translator ---------------------------------------------------
export function detailed(expr) {
  try { return { ok: true, text: cronstrue.toString((expr || '').trim(), { verbose: false }) } }
  catch (e) { return { ok: false, text: 'Could not parse: ' + (e && e.message ? e.message : e) } }
}

// engine: 'auto' | 'cronstrue' | 'simple'
export function describeCron(expr, engine = 'auto') {
  if (engine === 'simple') return { ...handRolled(expr), source: 'built-in' }
  if (engine === 'cronstrue') return { ...detailed(expr), source: 'cronstrue' }
  const d = detailed(expr)
  if (d.ok) return { ...d, source: 'cronstrue' }
  return { ...handRolled(expr), source: 'built-in' } // auto fallback
}

// --- next run (client-side matcher: *, lists, ranges, steps on numeric fields) ---
function matchField(f, v, mn, mx) {
  if (f === '*') return true
  return f.split(',').some(part => {
    let step = 1, r = part
    if (part.includes('/')) { const a = part.split('/'); r = a[0]; step = parseInt(a[1], 10) || 1 }
    let lo, hi
    if (r === '*') { lo = mn; hi = mx }
    else if (r.includes('-')) { const b = r.split('-'); lo = +b[0]; hi = +b[1] }
    else { lo = hi = +r }
    if (isNaN(lo) || isNaN(hi)) return false
    if (v < lo || v > hi) return false
    if (step === 1) return true
    return (v - (r === '*' ? mn : lo)) % step === 0
  })
}
export function cronMatches(expr, d) {
  const p = (expr || '').trim().split(/\s+/)
  if (p.length !== 5) return false
  return matchField(p[0], d.getUTCMinutes(), 0, 59) &&
    matchField(p[1], d.getUTCHours(), 0, 23) &&
    matchField(p[2], d.getUTCDate(), 1, 31) &&
    matchField(p[3], d.getUTCMonth() + 1, 1, 12) &&
    matchField(p[4], d.getUTCDay(), 0, 6)
}
export function nextRun(expr) {
  const d = new Date(); d.setUTCSeconds(0, 0)
  for (let i = 0; i < 60 * 24 * 8; i++) {   // scan up to 8 days ahead
    d.setUTCMinutes(d.getUTCMinutes() + 1)
    if (cronMatches(expr, d)) return d
  }
  return null
}
export function formatNext(d) {
  if (!d) return ''
  const now = new Date(), tm = new Date(now); tm.setDate(now.getDate() + 1)
  const day = d.toDateString() === now.toDateString() ? 'Today'
    : d.toDateString() === tm.toDateString() ? 'Tomorrow'
    : d.toLocaleDateString()
  const utc = `${two(d.getUTCHours())}:${two(d.getUTCMinutes())} UTC`
  const loc = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return `next run ${day} ${utc}  ·  = ${loc} your time`
}
