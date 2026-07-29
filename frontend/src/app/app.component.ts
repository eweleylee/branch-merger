import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from './api.service';
import { busy as busySignal } from './busy';
import { MergePanelComponent } from './merge-panel.component';
import { ScheduleListComponent } from './schedule-list.component';
import { NotificationBellComponent } from './notification-bell.component';
import { SettingsPanelComponent } from './settings-panel.component';
import { LogViewerComponent } from './log-viewer.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, MergePanelComponent, ScheduleListComponent, NotificationBellComponent, SettingsPanelComponent, LogViewerComponent],
  template: `
  <div class="wrap">
    <header>
      <div class="title-block">
        <h1>🌿 Branch Merger</h1>
        <p class="sub">Choose a source and target branch, merge instantly, or schedule it.</p>
      </div>
      <div class="header-actions">
        <app-notification-bell [items]="notifications" [unread]="unread" (changed)="loadNotifications()"></app-notification-bell>
        <button class="gear" [disabled]="busy()" (click)="showLogs = true" aria-label="View logs" title="View logs">📄</button>
        <button class="gear" [disabled]="busy()" (click)="showSettings = true" aria-label="Settings">⚙️</button>
      </div>
    </header>

    <div *ngIf="updateInfo?.updateAvailable && !updateDismissed && !updating" class="update-banner">
      <span><strong>Update available</strong> — {{ updateInfo.latestVersion }} (you have {{ updateInfo.currentVersion }}).</span>
      <button *ngIf="updateInfo.canSelfUpdate" class="update-now" (click)="applyUpdate()">Update now</button>
      <a *ngIf="!updateInfo.canSelfUpdate && updateInfo.url" [href]="updateInfo.url" target="_blank" rel="noopener" class="link">Download</a>
      <span *ngIf="updateError" class="update-err">{{ updateError }}</span>
      <button class="dismiss" (click)="updateDismissed = true" aria-label="Dismiss">✕</button>
    </div>

    <div *ngIf="updating" class="update-overlay">
      <div class="update-modal" role="dialog" aria-modal="true" aria-live="polite">
        <div *ngIf="!updateError" class="spinner"></div>
        <h2>{{ updateError ? 'Update interrupted' : 'Updating Branch Merger' }}</h2>
        <p *ngIf="updateInfo" class="ver">Version {{ updateInfo.latestVersion }}</p>
        <p *ngIf="!updateError" class="status">{{ updateStatus }}</p>
        <p *ngIf="!updateError" class="sub">The app will restart and this page reloads automatically. Please don’t close this window.</p>
        <p *ngIf="updateError" class="err">{{ updateError }}</p>
        <button *ngIf="canReload" class="btn-primary reload-btn" (click)="reloadNow()">Reload now</button>
      </div>
    </div>

    <div *ngIf="repoStatus && !repoStatus.ready" class="banner">
      Repository not ready: {{ repoStatus.message }}
      <button class="link" (click)="showSettings = true">Open Settings</button>
    </div>
    <div *ngIf="(!repoStatus || repoStatus.ready) && branchError" class="banner">
      Could not read branches: {{ branchError }}
      <button class="link" (click)="showSettings = true">Open Settings</button>
    </div>

    <div class="grid">
      <app-merge-panel [branches]="branches" [branchesUpdatedAt]="branchesUpdatedAt" [refreshing]="refreshing"
        (refresh)="forceRefresh()" (scheduled)="loadSchedules()"></app-merge-panel>
      <app-schedule-list [schedules]="schedules" (changed)="loadSchedules()"></app-schedule-list>
    </div>

    <footer>
      Backend fetches branches continuously in the background · schedules run server-side even if this tab is closed.
    </footer>

    <app-settings-panel *ngIf="showSettings" (close)="showSettings = false" (saved)="onSettingsSaved()"></app-settings-panel>
    <app-log-viewer *ngIf="showLogs" (close)="showLogs = false"></app-log-viewer>
  </div>
  `,
  styles: [`
  .wrap { max-width: 1400px; margin: 0 auto; padding: 40px 32px 60px; }
  header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
  h1 { margin: 0; font-size: 26px; letter-spacing: -.3px; }
  .sub { margin: 6px 0 0; color: var(--muted); }
  .header-actions { display: flex; align-items: center; gap: 10px; }
  .gear { font-size: 18px; width: 42px; height: 42px; border-radius: 50%; padding: 0; display: grid; place-items: center; background: var(--panel-2); }
  .banner .link { background: transparent; border: none; color: var(--accent); padding: 0 0 0 8px; font: inherit; cursor: pointer; text-decoration: underline; }
  .update-banner { display: flex; align-items: center; gap: 12px; margin-bottom: 20px; background: rgba(79,140,255,.12); border: 1px solid var(--accent); color: #cfe0ff; padding: 10px 14px; border-radius: 8px; font-size: 14px; }
  .update-banner .link { color: var(--accent); text-decoration: underline; }
  .update-banner .update-now { background: var(--accent); border: none; color: #fff; padding: 5px 12px; border-radius: 6px; font: inherit; font-weight: 600; cursor: pointer; }
  .update-banner .update-now:hover { filter: brightness(1.08); }
  .update-banner .update-err { color: var(--danger); font-size: 13px; }
  .update-banner .dismiss { margin-left: auto; background: transparent; border: none; color: var(--muted); cursor: pointer; font-size: 14px; padding: 2px 6px; }
  .update-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,.6); backdrop-filter: blur(2px); display: grid; place-items: center; padding: 20px; }
  .update-modal { background: var(--panel); border: 1px solid var(--accent); border-radius: 14px; padding: 30px 34px; max-width: 440px; text-align: center; box-shadow: 0 24px 64px rgba(0,0,0,.5); }
  .update-modal .spinner { width: 38px; height: 38px; border-width: 3px; margin: 0 auto 18px; color: var(--accent); display: block; }
  .update-modal h2 { margin: 0 0 6px; font-size: 20px; }
  .update-modal .ver { margin: 0 0 14px; color: var(--muted); font-size: 13px; }
  .update-modal .status { margin: 0 0 10px; color: var(--text); font-size: 15px; font-weight: 600; }
  .update-modal .sub { margin: 0; color: var(--muted); font-size: 13px; line-height: 1.5; }
  .update-modal .err { margin: 8px 0 0; color: var(--danger); font-size: 14px; }
  .update-modal .reload-btn { margin-top: 18px; }
  .banner { background: rgba(224,160,58,.12); border: 1px solid var(--warn); color: #f0d3a0; padding: 12px 14px; border-radius: 8px; margin-bottom: 20px; font-size: 14px; }
  .grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 20px; align-items: start; }
  @media (max-width: 900px) { .grid { grid-template-columns: minmax(0, 1fr); } }
  footer { margin-top: 28px; color: var(--muted); font-size: 12px; text-align: center; }
  `]
})
export class AppComponent implements OnInit, OnDestroy {
  readonly busy = busySignal;

  branches: any[] = [];
  branchesUpdatedAt: string | null = null;
  branchError: string | null = null;
  refreshing = false;
  schedules: any[] = [];
  notifications: any[] = [];
  unread = 0;
  showSettings = false;
  showLogs = false;
  repoStatus: any = null;
  updateInfo: any = null;
  updateDismissed = false;
  updating = false;
  updateError: string | null = null;
  updateStartedAt: Date | null = null;
  canReload = false;

  private pollTimer: any = null;
  private updateTimer: any = null;

  constructor(private api: ApiService) {}

  get updateStatus(): string {
    if (!this.updateStartedAt) return '';
    const n = this.notifications.find(x => x.trigger === 'Update' && new Date(x.createdUtc) >= this.updateStartedAt!);
    return n?.message || 'Downloading the update…';
  }

  ngOnInit() {
    this.loadBranches();
    this.loadSchedules();
    this.loadNotifications();
    this.loadRepoStatus();
    this.loadUpdate();
    this.pollTimer = setInterval(() => { this.loadBranches(); this.loadSchedules(); this.loadNotifications(); }, 10000);
    this.updateTimer = setInterval(() => this.loadUpdate(), 60 * 60 * 1000);
  }
  ngOnDestroy() { clearInterval(this.pollTimer); clearInterval(this.updateTimer); }

  async loadRepoStatus() { try { this.repoStatus = await this.api.getRepoStatus(); } catch {} }
  async loadUpdate() { try { this.updateInfo = await this.api.getUpdate(); } catch {} }

  async loadBranches() {
    try {
      const data = await this.api.getBranches();
      this.branches = data.branches || [];
      this.branchesUpdatedAt = data.lastUpdatedUtc;
      this.branchError = data.lastError;
    } catch (e: any) { this.branchError = e.message; }
  }

  async forceRefresh() {
    this.refreshing = true;
    try {
      const data = await this.api.refreshBranches();
      this.branches = data.branches || [];
      this.branchesUpdatedAt = data.lastUpdatedUtc;
    } catch (e: any) { this.branchError = e.message; }
    finally { this.refreshing = false; }
  }

  async loadSchedules() { try { this.schedules = await this.api.getSchedules(); } catch {} }
  async loadNotifications() {
    try { const data = await this.api.getNotifications(); this.notifications = data.items || []; this.unread = data.unread || 0; } catch {}
  }

  onSettingsSaved() { this.loadRepoStatus(); this.forceRefresh(); }

  async applyUpdate() {
    this.updating = true;
    this.updateError = null;
    this.canReload = false;
    this.updateStartedAt = new Date(Date.now() - 3000);
    try { await this.api.applyUpdate(); this.watchForRestart(); }
    catch (e: any) { this.updateError = e.message; this.updating = false; }
  }

  reloadNow() { window.location.reload(); }

  private watchForRestart() {
    let sawDown = false;
    let waited = 0;
    const step = 2000;
    const cap = 10 * 60 * 1000;
    const iv = setInterval(async () => {
      waited += step;
      try { await this.api.getUpdate(); if (sawDown) { clearInterval(iv); window.location.reload(); } }
      catch { sawDown = true; }
      this.loadNotifications();
      if (waited >= cap) {
        clearInterval(iv);
        this.updateError = 'Update is taking longer than expected. Reload the page once the app has restarted.';
        this.canReload = true;
      }
    }, step);
  }
}
