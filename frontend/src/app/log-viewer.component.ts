import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './api.service';

@Component({
  selector: 'app-log-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="overlay" (click)="onOverlay($event)">
    <div class="modal">
      <div class="modal-head">
        <h2>📄 Logs</h2>
        <div class="head-controls">
          <select *ngIf="files.length" [(ngModel)]="selected" (change)="loadEntries()">
            <option *ngFor="let f of files" [value]="f.name">{{ fileLabel(f) }}</option>
          </select>
          <button class="btn-ghost small" [disabled]="loading" (click)="selected ? loadEntries() : loadFiles()">
            <span *ngIf="loading" class="spinner"></span>↻
          </button>
          <button class="btn-ghost" (click)="close.emit()">✕</button>
        </div>
      </div>

      <div class="body">
        <p class="hint">Newest first. Errors and merge conflicts are recorded here; files older than 30 days are removed automatically.</p>

        <div *ngIf="error" class="msg err">{{ error }}</div>
        <div *ngIf="!error && !files.length && !loading" class="empty">No logs yet — nothing has been recorded.</div>
        <div *ngIf="!error && files.length && !entries.length && !loading" class="empty">This log is empty.</div>

        <div *ngIf="!error && entries.length" class="entries">
          <div *ngFor="let e of entries" class="entry">
            <div class="meta">
              <span class="time">{{ e.time }}</span>
              <span class="lvl" [ngClass]="levelClass(e.level)">{{ e.level }}</span>
            </div>
            <pre class="text">{{ e.text }}</pre>
          </div>
          <p *ngIf="truncated" class="hint">Showing the {{ entries.length }} most recent of {{ total }} entries.</p>
        </div>
      </div>
    </div>
  </div>
  `,
  styles: [`
  .overlay { position: fixed; inset: 0; background: rgba(0,0,0,.55); display: grid; place-items: center; z-index: 50; padding: 20px; }
  .modal { width: 100%; max-width: 860px; max-height: 90vh; display: flex; flex-direction: column; background: var(--panel); border: 1px solid var(--border); border-radius: 14px; box-shadow: 0 24px 64px rgba(0,0,0,.5); }
  .modal-head { display: flex; align-items: center; gap: 12px; padding: 16px 20px; border-bottom: 1px solid var(--border); }
  .modal-head h2 { margin: 0; font-size: 18px; flex: 1; }
  .head-controls { display: flex; align-items: center; gap: 8px; }
  .head-controls select { width: auto; min-width: 180px; }
  .small { padding: 6px 10px; font-size: 13px; }
  .body { padding: 12px 20px 16px; overflow: auto; }
  .hint { color: var(--muted); font-size: 12px; margin: 0 0 12px; }
  .empty { color: var(--muted); font-size: 14px; padding: 24px 0; text-align: center; }
  .entries { display: flex; flex-direction: column; gap: 8px; }
  .entry { border: 1px solid var(--border); border-radius: 8px; padding: 8px 10px; background: var(--panel-2); }
  .meta { display: flex; align-items: center; gap: 10px; margin-bottom: 4px; }
  .time { color: var(--muted); font-size: 12px; font-variant-numeric: tabular-nums; }
  .lvl { font-size: 11px; font-weight: 700; padding: 1px 8px; border-radius: 6px; }
  .lvl.err { color: #fff; background: var(--danger); }
  .lvl.warn { color: #06231a; background: var(--warn); }
  .lvl.info { color: var(--muted); background: var(--panel); border: 1px solid var(--border); }
  .text { margin: 0; white-space: pre-wrap; word-break: break-word; font-size: 12.5px; color: var(--text); }
  .msg { margin: 4px 0; padding: 10px 12px; border-radius: 8px; font-size: 13px; }
  .msg.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
  `]
})
export class LogViewerComponent {
  @Output() close = new EventEmitter<void>();

  files: any[] = [];
  selected = '';
  entries: any[] = [];
  total = 0;
  truncated = false;
  loading = false;
  error: string | null = null;

  constructor(private api: ApiService) {}

  ngOnInit() { this.loadFiles(); }

  onOverlay(e: MouseEvent) { if (e.target === e.currentTarget) this.close.emit(); }

  async loadFiles() {
    this.loading = true; this.error = null;
    try {
      const data = await this.api.getLogs();
      this.files = data.files || [];
      if (this.files.length) { this.selected = this.files[0].name; await this.loadEntries(); }
      else this.entries = [];
    } catch (e: any) { this.error = e.message; } finally { this.loading = false; }
  }

  async loadEntries() {
    if (!this.selected) return;
    this.loading = true; this.error = null;
    try {
      const data = await this.api.getLog(this.selected);
      this.entries = data.entries || [];
      this.total = data.total || 0;
      this.truncated = !!data.truncated;
    } catch (e: any) { this.error = e.message; } finally { this.loading = false; }
  }

  levelClass(level: string) {
    if (level === 'Error' || level === 'Critical') return 'err';
    if (level === 'Warning') return 'warn';
    return 'info';
  }
  fmtSize(n: number) {
    if (n < 1024) return n + ' B';
    if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
    return (n / 1024 / 1024).toFixed(1) + ' MB';
  }
  fileLabel(f: any) { return `${f.date}  (${this.fmtSize(f.sizeBytes)})`; }
}
