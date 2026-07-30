import { Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './api.service';
import { busy as busySignal } from './busy';
import { BranchSelectComponent } from './branch-select.component';
import { describeCron, nextRun, formatNext, CronInfo } from './cron';

@Component({
  selector: 'app-merge-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, BranchSelectComponent],
  template: `
  <section class="card">
    <div class="head">
      <h2>Merge branches</h2>
      <div class="updated">
        <span *ngIf="branchesUpdatedAt">branches updated {{ formatTime(branchesUpdatedAt) }}</span>
        <button class="btn-ghost small" [disabled]="refreshing || busy()" (click)="doRefresh()">
          <span *ngIf="refreshing" class="spinner"></span>{{ refreshing ? 'Refreshing…' : '↻ Refresh' }}
        </button>
      </div>
    </div>

    <div class="merge-row">
      <div class="field">
        <label>Merge from (source)</label>
        <app-branch-select [(value)]="source" [branches]="branchOptions" [disabled]="busy()" placeholder="Search branches…"></app-branch-select>
      </div>
      <div class="arrow">→</div>
      <div class="field">
        <label>Merge into (target)</label>
        <app-branch-select [(value)]="target" [branches]="branchOptions" [disabled]="busy()" placeholder="Search branches…"></app-branch-select>
      </div>
    </div>

    <label class="check">
      <input type="checkbox" [(ngModel)]="push" [disabled]="busy()" />
      Push target to remote after a successful merge
    </label>

    <div class="modes">
      <button class="seg" [class.seg-on]="mode==='now'"  [disabled]="busy()" (click)="mode='now'">Merge now</button>
      <button class="seg" [class.seg-on]="mode==='once'" [disabled]="busy()" (click)="mode='once'">Schedule once</button>
      <button class="seg" [class.seg-on]="mode==='cron'" [disabled]="busy()" (click)="mode='cron'">Recurring</button>
    </div>

    <div *ngIf="mode==='once'" class="field">
      <label>Run at (your local time)</label>
      <input type="datetime-local" [(ngModel)]="runAtLocal" />
    </div>

    <div *ngIf="mode==='cron'" class="field">
      <label>Cron expression (local time · minute hour day month weekday)</label>
      <input type="text" [(ngModel)]="cron" placeholder="0 2 * * *" />
      <div class="cron-echo" [class.bad]="!cronInfo.ok">
        <span class="ic" aria-hidden="true">{{ cronInfo.ok ? '✓' : '⚠' }}</span>
        <span>{{ cronInfo.text }}</span>
      </div>
      <div *ngIf="cronNext" class="cron-next">{{ cronNext }}</div>
      <p class="hint">
        Examples: <code>*/30 * * * *</code> every 30 min ·
        <code>0 2 * * *</code> daily 02:00 ·
        <code>0 8 * * 1</code> Mondays 08:00
      </p>
    </div>

    <div class="actions">
      <button [class.btn-success]="mode==='now'" [class.btn-primary]="mode!=='now'"
        [disabled]="!canSubmit" (click)="submit()">
        <span *ngIf="busy()"><span class="spinner"></span>Working…</span>
        <span *ngIf="!busy() && mode==='now'">Merge now</span>
        <span *ngIf="!busy() && mode!=='now'">Create schedule</span>
      </button>
    </div>

    <div *ngIf="busy() || refreshing || processLines.length" class="process">
      <div class="process-head">
        <span *ngIf="busy() || refreshing" class="spinner"></span>
        <strong>{{ processHeading }}</strong>
      </div>
      <pre #processBox>{{ processLines.join('\n') }}</pre>
    </div>

    <div *ngIf="result" class="result" [class.ok]="result.ok" [class.err]="!result.ok">
      <strong>{{ result.ok ? 'Success' : 'Problem' }}:</strong> {{ result.message }}
      <ul *ngIf="result.conflictedFiles?.length" class="conflicts">
        <li *ngFor="let f of result.conflictedFiles"><code>{{ f }}</code></li>
      </ul>
    </div>
  </section>
  `,
  styles: [`
  .card { background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 22px; }
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
  .process { margin-top: 16px; }
  .process-head { display: flex; align-items: center; gap: 8px; font-size: 14px; color: var(--muted); margin-bottom: 8px; }
  .process pre {
    margin: 0; white-space: pre-wrap; font-size: 12px; color: var(--muted);
    background: var(--panel-2); border: 1px solid var(--border); border-radius: 8px;
    padding: 10px 12px; max-height: 260px; overflow: auto;
  }
  .result { margin-top: 16px; padding: 12px 14px; border-radius: 8px; font-size: 14px; }
  .result.ok { background: rgba(63,178,127,.12); border: 1px solid var(--accent-2); }
  .result.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
  .conflicts { margin: 8px 0 0; padding-left: 18px; }
  .conflicts li { font-size: 13px; margin: 2px 0; }
  .conflicts code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }
  `]
})
export class MergePanelComponent {
  @Input() branches: any[] = [];
  @Input() branchesUpdatedAt: string | null = null;
  @Output() refreshed = new EventEmitter<any>();   // { branches, lastUpdatedUtc } from a fetch
  @Output() scheduled = new EventEmitter<void>();

  @ViewChild('processBox') processBox?: ElementRef<HTMLElement>;

  readonly busy = busySignal;
  refreshing = false;
  processKind: 'merge' | 'fetch' = 'merge';   // which activity the shared process box shows

  source = '';
  target = '';
  push = true;
  mode: 'now' | 'once' | 'cron' = 'now';
  runAtLocal = '';
  cron = '0 2 * * *';

  result: any = null;
  processLines: string[] = [];

  constructor(private api: ApiService) {}

  get branchOptions(): any[] { return [...this.branches].sort((a, b) => (b.isRemote - a.isRemote)); }
  get cronInfo(): CronInfo { return describeCron(this.cron); }
  get cronNext(): string { return this.cronInfo.ok ? formatNext(nextRun(this.cron)) : ''; }
  get canSubmit(): boolean { return !!this.source && !!this.target && this.source !== this.target && !this.busy(); }

  // Heading for the shared process box (used by both merge and refresh).
  get processHeading(): string {
    if (this.busy()) return 'Merging…';
    if (this.refreshing) return 'Fetching branches…';
    return this.processKind === 'fetch' ? 'Last fetch' : 'Merge process';
  }

  // Refresh runs the fetch and streams its git step into the same process box as merges.
  async doRefresh() {
    this.refreshing = true;
    this.processKind = 'fetch';
    this.result = null;
    this.processLines = [];
    try {
      await this.api.refreshStream(
        line => this.pushLine(line),
        res => this.refreshed.emit(res)
      );
    } catch (e: any) {
      this.result = { ok: false, message: e.message };
    } finally {
      this.refreshing = false;
    }
  }

  formatTime(iso: string) { return new Date(iso).toLocaleTimeString(); }

  private pushLine(line: string) {
    this.processLines.push(line);
    setTimeout(() => { const el = this.processBox?.nativeElement; if (el) el.scrollTop = el.scrollHeight; });
  }

  async mergeNow() {
    this.busy.set(true);
    this.result = null;
    this.processLines = [];
    this.processKind = 'merge';
    try {
      await this.api.mergeStream(
        { sourceBranch: this.source, targetBranch: this.target, push: this.push },
        line => this.pushLine(line),
        res => {
          this.result = res.success
            ? { ok: true, message: res.message }
            : { ok: false, message: res.message, conflictedFiles: res.conflictedFiles || [] };
        }
      );
    } catch (e: any) {
      this.result = { ok: false, message: e.message };
    } finally {
      this.busy.set(false);
    }
  }

  async createSchedule() {
    this.busy.set(true); this.result = null;
    try {
      const payload: any = {
        sourceBranch: this.source, targetBranch: this.target, push: this.push,
        type: this.mode === 'once' ? 'Once' : 'Cron'
      };
      if (this.mode === 'once') {
        if (!this.runAtLocal) throw new Error('Pick a date and time.');
        payload.runAtUtc = new Date(this.runAtLocal).toISOString();
      } else {
        payload.cronExpression = this.cron.trim();
      }
      await this.api.createSchedule(payload);
      this.result = { ok: true, message: 'Schedule created.' };
      this.scheduled.emit();
    } catch (e: any) {
      this.result = { ok: false, message: e.message };
    } finally {
      this.busy.set(false);
    }
  }

  submit() { if (this.mode === 'now') this.mergeNow(); else this.createSchedule(); }
}
