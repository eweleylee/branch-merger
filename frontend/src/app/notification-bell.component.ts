import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from './api.service';
import { busy as busySignal } from './busy';

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule],
  template: `
  <div class="bell-wrap">
    <button class="bell" [disabled]="busy()" (click)="toggleOpen()" [attr.aria-label]="unread + ' unread notifications'">
      🔔
      <span *ngIf="unread > 0" class="badge">{{ unread > 99 ? '99+' : unread }}</span>
    </button>

    <div *ngIf="open" class="backdrop" (click)="close()"></div>

    <div *ngIf="open" class="panel">
      <div class="panel-head">
        <strong>Notifications</strong>
        <div class="panel-actions">
          <button class="link" (click)="sendTest()">Test</button>
          <button class="link" (click)="clearAll()" [disabled]="!items.length">Clear</button>
          <button class="link close" (click)="close()" aria-label="Close notifications">✕</button>
        </div>
      </div>

      <p *ngIf="!items.length" class="empty">Nothing yet. Conflict and failure alerts will show up here.</p>

      <ul *ngIf="items.length">
        <li *ngFor="let n of items" class="item" [ngClass]="n.level.toLowerCase()">
          <div class="row1">
            <span class="ico">{{ icon(n.level) }}</span>
            <span class="title">{{ n.title }}</span>
            <span class="time">{{ fmt(n.createdUtc) }}</span>
          </div>
          <div *ngIf="n.sourceBranch && n.targetBranch" class="branches">
            <span class="src">{{ n.sourceBranch }}</span> →
            <span class="tgt">{{ n.targetBranch }}</span>
            <span *ngIf="n.trigger" class="trigger">· {{ n.trigger }}</span>
          </div>
          <div class="msg">{{ n.message }}</div>
          <ul *ngIf="n.conflictedFiles?.length" class="files">
            <li *ngFor="let f of n.conflictedFiles"><code>{{ f }}</code></li>
          </ul>
        </li>
      </ul>
    </div>
  </div>
  `,
  styles: [`
  .bell-wrap { position: relative; }
  .bell { position: relative; font-size: 18px; line-height: 1; padding: 0; width: 42px; height: 42px; border-radius: 50%; display: grid; place-items: center; background: var(--panel-2); }
  .badge { position: absolute; top: -4px; right: -4px; background: var(--danger); color: #fff; font-size: 11px; font-weight: 700; min-width: 18px; height: 18px; padding: 0 5px; border-radius: 9px; display: grid; place-items: center; }
  .panel { position: absolute; right: 0; top: 50px; width: 360px; max-width: calc(100vw - 24px); max-height: 460px; overflow: auto; background: var(--panel); border: 1px solid var(--border); border-radius: 12px; box-shadow: 0 16px 48px rgba(0,0,0,.45); z-index: 20; padding: 8px; }
  @media (max-width: 640px) { .panel { position: fixed; top: 64px; left: 12px; right: 12px; width: auto; max-width: none; max-height: 70vh; } }
  .panel-head { display: flex; justify-content: space-between; align-items: center; padding: 8px 10px; }
  .panel-actions { display: flex; gap: 6px; align-items: center; }
  .link { background: transparent; border: none; color: var(--accent); padding: 4px 6px; font-size: 12px; cursor: pointer; }
  .link:disabled { color: var(--muted); }
  .link.close { color: var(--muted); font-size: 14px; }
  .backdrop { position: fixed; inset: 0; z-index: 19; background: transparent; }
  .empty { color: var(--muted); font-size: 13px; padding: 14px 10px; }
  ul { list-style: none; margin: 0; padding: 0; }
  .item { padding: 10px; border-radius: 8px; border: 1px solid transparent; }
  .item + .item { margin-top: 4px; }
  .item.warning { background: rgba(224,160,58,.08); border-color: rgba(224,160,58,.3); }
  .item.error   { background: rgba(229,88,77,.08);  border-color: rgba(229,88,77,.3); }
  .item.info    { background: var(--panel-2); }
  .row1 { display: flex; align-items: center; gap: 8px; }
  .title { font-weight: 600; font-size: 14px; flex: 1; }
  .time { font-size: 11px; color: var(--muted); }
  .branches { font-size: 12px; margin: 4px 0 2px 26px; }
  .src { color: var(--accent); }
  .tgt { color: var(--accent-2); }
  .trigger { color: var(--muted); }
  .msg { font-size: 13px; color: var(--text); margin-left: 26px; }
  .files { margin: 6px 0 0 26px; }
  .files li { font-size: 12px; }
  .files code { background: var(--panel-2); padding: 1px 6px; border-radius: 5px; }
  `]
})
export class NotificationBellComponent {
  @Input() items: any[] = [];
  @Input() unread = 0;
  @Output() changed = new EventEmitter<void>();

  readonly busy = busySignal;
  open = false;

  constructor(private api: ApiService) {}

  close() { this.open = false; }
  async toggleOpen() {
    this.open = !this.open;
    if (this.open && this.unread > 0) { await this.api.markAllRead(); this.changed.emit(); }
  }
  async clearAll() { await this.api.clearNotifications(); this.changed.emit(); }
  async sendTest() { await this.api.testNotification(); this.changed.emit(); }

  icon(level: string) { return level === 'Error' ? '🔴' : level === 'Warning' ? '⚠️' : 'ℹ️'; }
  fmt(dt: string) { return new Date(dt).toLocaleString(); }
}
