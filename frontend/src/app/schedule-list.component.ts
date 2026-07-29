import { Component, EventEmitter, Input, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from './api.service';
import { busy as busySignal } from './busy';

@Component({
  selector: 'app-schedule-list',
  standalone: true,
  imports: [CommonModule],
  template: `
  <section class="card">
    <h2>Scheduled merges</h2>

    <p *ngIf="!schedules.length" class="empty">No schedules yet. Create one from the panel on the left.</p>

    <ng-container *ngIf="schedules.length">
      <p class="hint">
        Drag rows to reorder. Ordering only affects merges that run at the <strong>same time</strong>
        (shown grouped below); the top one in a group runs first.
      </p>

      <div class="sched-scroll">
        <div *ngFor="let group of groups" class="group">
          <div class="group-head">
            <span>{{ group.label }}</span>
            <span *ngIf="group.items.length > 1" class="tag">runs in this order ↓</span>
            <span *ngIf="group.items.length <= 1" class="tag muted">only one — order n/a</span>
          </div>

          <div *ngFor="let s of group.items; let i = index" class="row"
            [class.off]="!s.enabled" [class.orderable]="group.items.length > 1 && !busy()"
            [attr.draggable]="group.items.length > 1 && !busy()"
            (dragstart)="onDragStart(group.key, i)"
            (dragover)="$event.preventDefault()"
            (drop)="onDrop(group.key, i)">

            <span class="grip" *ngIf="group.items.length > 1" title="Drag to reorder">⠿</span>
            <span class="seq" *ngIf="group.items.length > 1">{{ i + 1 }}</span>

            <div class="content">
              <span class="branches">
                <span class="src">{{ s.sourceBranch }}</span>
                <span class="into">→</span>
                <span class="tgt">{{ s.targetBranch }}</span>
                <span *ngIf="s.push" class="pill">push</span>
              </span>

              <span class="status" [ngClass]="statusClass(s.lastStatus)">{{ s.lastStatus || 'not run yet' }}</span>

              <span class="actions">
                <button class="mini" [disabled]="busy() || togglingId===s.id" (click)="toggle(s.id)">
                  <span *ngIf="togglingId===s.id" class="spinner"></span>{{ s.enabled ? 'Pause' : 'Resume' }}
                </button>
                <button class="mini danger" [disabled]="busy() || deletingId===s.id" (click)="remove(s.id)">
                  <span *ngIf="deletingId===s.id" class="spinner"></span>Delete
                </button>
              </span>
            </div>
          </div>
        </div>
      </div>
    </ng-container>
  </section>
  `,
  styles: [`
  .card { background: var(--panel); border: 1px solid var(--border); border-radius: 12px; padding: 22px; min-width: 0; }
  h2 { margin: 0 0 12px; font-size: 18px; }
  .empty { color: var(--muted); font-size: 14px; }
  .hint { color: var(--muted); font-size: 12px; margin: 0 0 16px; }
  .group { margin-bottom: 16px; }
  .group-head { display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--muted); padding: 6px 4px; border-bottom: 1px solid var(--border); }
  .tag { margin-left: auto; font-size: 11px; }
  .tag.muted { opacity: .7; }
  .row { display: flex; align-items: flex-start; gap: 10px; padding: 12px 8px; border-bottom: 1px solid var(--border); font-size: 13px; }
  .row.orderable { cursor: grab; }
  .row.orderable:active { cursor: grabbing; }
  .row.off { opacity: .55; }
  .grip { color: var(--muted); font-size: 14px; letter-spacing: -2px; padding-top: 2px; flex: none; }
  .seq { width: 20px; height: 20px; border-radius: 50%; display: grid; place-items: center; background: var(--panel-2); color: var(--muted); font-size: 11px; flex: none; }
  .content { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; }
  .branches { min-width: 0; }
  .src { color: var(--accent); }
  .tgt { color: var(--accent-2); }
  .into { color: var(--muted); margin: 0 6px; }
  .pill { margin-left: 8px; font-size: 11px; background: var(--panel-2); padding: 1px 7px; border-radius: 6px; color: var(--muted); }
  .status { font-size: 12px; color: var(--muted); white-space: normal; word-break: break-word; }
  .status.ok { color: var(--accent-2); }
  .status.err { color: var(--danger); }
  .actions { display: flex; gap: 8px; margin-top: 2px; }
  .mini { background: transparent; border: 1px solid var(--border); color: var(--text); border-radius: 8px; padding: 5px 10px; font-size: 12px; cursor: pointer; }
  .mini.danger { border-color: var(--danger); color: var(--danger); }
  `]
})
export class ScheduleListComponent {
  @Input() schedules: any[] = [];
  @Output() changed = new EventEmitter<void>();

  readonly busy = busySignal;

  groups: any[] = [];
  private dragCtx: { groupKey: string; index: number } | null = null;
  togglingId: string | null = null;
  deletingId: string | null = null;

  constructor(private api: ApiService) {}

  ngOnChanges(_c: SimpleChanges) { this.groups = this.buildGroups(this.schedules || []); }

  private timeKeyOf(s: any) { return s.type === 'Once' ? `once:${s.runAtUtc}` : `cron:${s.cronExpression}`; }
  private timeLabelOf(s: any) {
    if (s.type === 'Once') return `Once · ${s.runAtUtc ? new Date(s.runAtUtc).toLocaleString() : '—'}`;
    return `Recurring · ${s.cronExpression} (local time)`;
  }
  private nextRunOf(s: any) { return s.nextRunUtc ? new Date(s.nextRunUtc).getTime() : Number.MAX_SAFE_INTEGER; }

  private buildGroups(list: any[]) {
    const map = new Map<string, any>();
    for (const s of list) {
      const key = this.timeKeyOf(s);
      if (!map.has(key)) map.set(key, { key, label: this.timeLabelOf(s), sortAt: this.nextRunOf(s), items: [] });
      map.get(key).items.push({ ...s });
    }
    const arr = [...map.values()];
    arr.forEach(g => g.items.sort((a: any, b: any) => (a.order - b.order)));
    arr.sort((a, b) => a.sortAt - b.sortAt);
    return arr;
  }

  onDragStart(groupKey: string, index: number) { this.dragCtx = { groupKey, index }; }

  onDrop(groupKey: string, index: number) {
    const ctx = this.dragCtx; this.dragCtx = null;
    if (!ctx || ctx.groupKey !== groupKey || ctx.index === index) return;
    const group = this.groups.find(g => g.key === groupKey);
    const items = group.items;
    const [moved] = items.splice(ctx.index, 1);
    items.splice(index, 0, moved);
    this.persistOrder();
  }

  private async persistOrder() {
    const ids = this.groups.flatMap(g => g.items.map((i: any) => i.id));
    await this.api.reorderSchedules(ids);
    this.changed.emit();
  }

  async toggle(id: string) {
    this.togglingId = id;
    try { await this.api.toggleSchedule(id); this.changed.emit(); }
    finally { this.togglingId = null; }
  }
  async remove(id: string) {
    this.deletingId = id;
    try { await this.api.deleteSchedule(id); this.changed.emit(); }
    finally { this.deletingId = null; }
  }

  statusClass(st: string) { return !st ? '' : (st.startsWith('Success') ? 'ok' : 'err'); }
}
