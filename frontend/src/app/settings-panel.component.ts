import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, ApiError } from './api.service';

@Component({
  selector: 'app-settings-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="overlay" (click)="onOverlay($event)">
    <div class="modal">
      <div class="modal-head">
        <h2>⚙️ Settings</h2>
        <button class="btn-ghost" (click)="close.emit()">✕</button>
      </div>

      <div *ngIf="loading" class="loading">Loading…</div>

      <div *ngIf="!loading" class="body">
        <section>
          <h3>Repository</h3>
          <div class="field">
            <label>Working clone path (a dedicated folder this app owns)</label>
            <input type="text" [(ngModel)]="s.git.repositoryPath" placeholder="C:\\merger\\repo-clone" />
          </div>
          <div class="field">
            <label>Repository URL (used to clone if the path is empty)</label>
            <input type="text" [(ngModel)]="s.git.repositoryUrl" placeholder="git@github.com:org/repo.git" />
          </div>
          <div class="two">
            <div class="field">
              <label>Remote name</label>
              <input type="text" [(ngModel)]="s.git.remoteName" placeholder="origin" />
            </div>
            <div class="field">
              <label>Fetch interval (seconds)</label>
              <input type="number" min="10" [(ngModel)]="s.git.fetchIntervalSeconds" />
            </div>
          </div>
          <div class="field">
            <label>Default branch (checked out after each merge · empty = stay on target)</label>
            <input type="text" [(ngModel)]="s.git.defaultBranch" placeholder="master" />
          </div>

          <div class="repo-status" *ngIf="repoStatus">
            <span class="dot" [class.on]="repoStatus.ready" [class.off]="!repoStatus.ready"></span>
            {{ repoStatus.message }}
            <span *ngIf="repoStatus.currentBranch" class="muted"> · on {{ repoStatus.currentBranch }}</span>
          </div>
          <div class="row-btns">
            <button class="btn-ghost small" [disabled]="checking" (click)="checkStatus()">
              <span *ngIf="checking" class="spinner"></span>{{ checking ? 'Checking…' : 'Check status' }}
            </button>
            <button class="btn-primary small" [disabled]="cloning" (click)="cloneRepo()">
              <span *ngIf="cloning" class="spinner"></span>{{ cloning ? 'Cloning…' : 'Clone / initialize' }}
            </button>
          </div>
        </section>

        <section>
          <h3>Startup</h3>
          <label class="check">
            <input type="checkbox" [(ngModel)]="s.runOnStartup" />
            Start automatically when Windows starts
          </label>
          <p class="hint">Runs in the background (no console window). Applies to the installed app only.</p>
        </section>

        <div *ngIf="message" class="msg" [class.ok]="message.ok" [class.err]="!message.ok">{{ message.text }}</div>
      </div>

      <div class="modal-foot">
        <div class="spacer"></div>
        <button class="btn-ghost" (click)="close.emit()">Close</button>
        <button class="btn-primary" [disabled]="saving" (click)="save()">
          <span *ngIf="saving" class="spinner"></span>{{ saving ? 'Saving…' : 'Save settings' }}
        </button>
      </div>
    </div>
  </div>
  `,
  styles: [`
  .overlay { position: fixed; inset: 0; background: rgba(0,0,0,.55); display: grid; place-items: center; z-index: 50; padding: 20px; }
  .modal { width: 100%; max-width: 640px; max-height: 90vh; display: flex; flex-direction: column; background: var(--panel); border: 1px solid var(--border); border-radius: 14px; box-shadow: 0 24px 64px rgba(0,0,0,.5); }
  .modal-head, .modal-foot { display: flex; align-items: center; gap: 10px; padding: 16px 20px; }
  .modal-head { border-bottom: 1px solid var(--border); }
  .modal-foot { border-top: 1px solid var(--border); }
  .modal-head h2 { margin: 0; font-size: 18px; flex: 1; }
  .spacer { flex: 1; }
  .loading { padding: 40px; text-align: center; color: var(--muted); }
  .body { padding: 8px 20px 4px; overflow: auto; }
  section { padding: 14px 0; border-bottom: 1px solid var(--border); }
  section:last-of-type { border-bottom: none; }
  h3 { margin: 0 0 12px; font-size: 14px; color: var(--accent); }
  .field { margin-bottom: 12px; }
  .two { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
  .check { display: flex; align-items: center; gap: 8px; color: var(--text); font-size: 14px; margin: 4px 0 12px; }
  .check input { width: auto; }
  .repo-status { font-size: 13px; margin: 6px 0 10px; display: flex; align-items: center; gap: 8px; }
  .dot { width: 9px; height: 9px; border-radius: 50%; display: inline-block; }
  .dot.on { background: var(--accent-2); }
  .dot.off { background: var(--warn); }
  .muted { color: var(--muted); }
  .row-btns { display: flex; gap: 8px; }
  .small { padding: 6px 12px; font-size: 12px; }
  .hint { font-size: 12px; color: var(--muted); margin: 8px 0 0; }
  .msg { margin: 14px 0 4px; padding: 10px 12px; border-radius: 8px; font-size: 13px; }
  .msg.ok { background: rgba(63,178,127,.12); border: 1px solid var(--accent-2); }
  .msg.err { background: rgba(229,88,77,.12); border: 1px solid var(--danger); }
  `]
})
export class SettingsPanelComponent {
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  loading = true;
  saving = false;
  cloning = false;
  checking = false;
  message: any = null;
  repoStatus: any = null;

  s: any = {
    git: { repositoryPath: '', repositoryUrl: '', remoteName: 'origin', fetchIntervalSeconds: 60, defaultBranch: 'master' },
    runOnStartup: true
  };

  constructor(private api: ApiService) {}

  ngOnInit() { this.load(); }

  onOverlay(e: MouseEvent) { if (e.target === e.currentTarget) this.close.emit(); }

  async load() {
    this.loading = true;
    try {
      const data = await this.api.getSettings();   // fast: just JSON, no git
      this.s = data.settings;
    } catch (e: any) {
      this.message = { ok: false, text: e.message };
    } finally {
      this.loading = false;   // show the form right away
    }
    // No repo-status check on open — it waits behind the git lock and can stall the panel.
    // The user runs it explicitly with the "Check status" button.
  }

  // The "Check status" button: apply what's on screen first, then check — so it reflects the
  // path/URL you just typed without a separate Save (essential on first-time setup).
  async checkStatus() {
    this.checking = true; this.message = null;
    try {
      await this.api.saveSettings(this.s);
      this.repoStatus = await this.api.getRepoStatus();
      this.saved.emit();   // settings were persisted; let the app pick up the new repo
    } catch (e: any) {
      this.repoStatus = { ready: false, message: e.message };
    } finally { this.checking = false; }
  }

  async save() {
    this.saving = true; this.message = null;
    try {
      const data = await this.api.saveSettings(this.s);
      this.s = data.settings;
      this.message = { ok: true, text: 'Settings saved.' };
      this.saved.emit();
    } catch (e: any) { this.message = { ok: false, text: e.message }; }
    finally { this.saving = false; }
  }

  async cloneRepo() {
    this.cloning = true; this.message = null;
    try {
      await this.api.saveSettings(this.s);
      this.repoStatus = await this.api.cloneRepo();
      this.message = { ok: this.repoStatus.ready, text: this.repoStatus.message };
      this.saved.emit();
    } catch (e: any) {
      const data = e instanceof ApiError ? e.data : null;
      this.message = { ok: false, text: data?.message || e.message };
      if (data) this.repoStatus = data;
    } finally { this.cloning = false; }
  }
}
