import { Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-branch-select',
  standalone: true,
  imports: [CommonModule],
  template: `
  <div class="branch-select" [class.disabled]="disabled">
    <div class="control" [class.open]="open">
      <input #inputEl type="text" role="combobox" aria-autocomplete="list"
        [attr.aria-expanded]="open"
        [value]="shownValue"
        [placeholder]="selected ? selected.shortName : placeholder"
        [disabled]="disabled"
        (focus)="openList()" (click)="openList()" (input)="onInput($event)"
        (keydown.arrowdown)="$event.preventDefault(); move(1)"
        (keydown.arrowup)="$event.preventDefault(); move(-1)"
        (keydown.enter)="$event.preventDefault(); enter()"
        (keydown.escape)="$event.preventDefault(); close()" />
      <span class="chev" aria-hidden="true"
        (mousedown)="$event.preventDefault(); disabled ? null : (open ? close() : inputEl.focus())">▾</span>
    </div>

    <ul *ngIf="open" class="list" role="listbox">
      <li *ngIf="!filtered.length" class="empty">No matching branches</li>
      <li *ngFor="let b of filtered; let i = index"
        class="opt" [class.active]="i === activeIndex" [class.chosen]="b.name === value"
        role="option" [attr.aria-selected]="b.name === value"
        (mouseenter)="activeIndex = i" (mousedown)="$event.preventDefault(); choose(b)">
        <span class="nm">{{ b.shortName }}</span>
      </li>
    </ul>
  </div>
  `,
  styles: [`
  .branch-select { position: relative; }
  .branch-select.disabled { opacity: .55; }
  .branch-select.disabled .chev { cursor: not-allowed; }
  .control { position: relative; }
  .control input:disabled { cursor: not-allowed; }
  .control input {
    width: 100%; padding: 9px 30px 9px 11px;
    color: var(--text); background: var(--panel); border: 1px solid var(--border);
    border-radius: 8px; outline: none; font: inherit;
  }
  .control input:focus { border-color: var(--accent); }
  .control.open input { border-color: var(--accent); }
  .chev { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); color: var(--muted); cursor: pointer; font-size: 12px; }
  .list {
    position: absolute; z-index: 25; left: 0; right: 0; top: calc(100% + 4px);
    margin: 0; padding: 4px; list-style: none;
    max-height: 240px; overflow-y: auto;
    background: var(--panel); border: 1px solid var(--border); border-radius: 8px;
    box-shadow: 0 12px 36px rgba(0,0,0,.45);
  }
  .opt {
    display: flex; align-items: center; gap: 8px; justify-content: space-between;
    padding: 8px 10px; border-radius: 6px; cursor: pointer; font-size: 13px;
  }
  .opt.active { background: var(--panel-2); }
  .opt.chosen .nm { color: var(--accent); }
  .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .empty { padding: 10px; color: var(--muted); font-size: 13px; }
  `]
})
export class BranchSelectComponent {
  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();
  @Input() branches: any[] = [];
  @Input() placeholder = 'Search branches…';
  @Input() disabled = false;

  @ViewChild('inputEl') inputEl!: ElementRef<HTMLInputElement>;

  open = false;
  query = '';
  activeIndex = -1;

  constructor(private host: ElementRef) {}

  get filtered(): any[] {
    const q = this.query.trim().toLowerCase();
    const list = [...this.branches].sort((a, b) => (b.isRemote - a.isRemote));
    if (!q) return list;
    return list.filter(b => b.shortName.toLowerCase().includes(q) || b.name.toLowerCase().includes(q));
  }
  get selected(): any { return this.branches.find(b => b.name === this.value) || null; }
  get displayLabel(): string { return this.selected ? this.selected.shortName : ''; }
  get shownValue(): string { return this.open ? this.query : this.displayLabel; }

  openList() {
    if (this.open || this.disabled) return;
    this.open = true; this.query = '';
    this.activeIndex = this.filtered.findIndex(b => b.name === this.value);
  }
  close() { this.open = false; this.query = ''; this.activeIndex = -1; }
  choose(b: any) { this.value = b.name; this.valueChange.emit(b.name); this.close(); this.inputEl?.nativeElement.blur(); }
  onInput(e: any) { if (!this.open) this.open = true; this.query = e.target.value; this.activeIndex = this.filtered.length ? 0 : -1; }
  move(delta: number) {
    if (!this.open) { this.openList(); return; }
    const n = this.filtered.length; if (!n) return;
    this.activeIndex = (this.activeIndex + delta + n) % n;
    setTimeout(() => this.host.nativeElement.querySelector('.opt.active')?.scrollIntoView({ block: 'nearest' }));
  }
  enter() { if (this.open && this.activeIndex >= 0 && this.filtered[this.activeIndex]) this.choose(this.filtered[this.activeIndex]); }

  @HostListener('document:mousedown', ['$event'])
  onDocDown(e: MouseEvent) { if (!this.host.nativeElement.contains(e.target)) this.close(); }

  ngOnChanges() {
    if (this.value && this.branches.length && !this.branches.some(b => b.name === this.value)) {
      this.value = ''; this.valueChange.emit('');
    }
  }
}
