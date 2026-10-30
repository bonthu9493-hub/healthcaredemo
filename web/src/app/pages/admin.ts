import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, AuditRow, PAGE_SIZE, UserRow, errorText } from '../core/api';
import { Pager } from '../ui/pager';

@Component({
  imports: [FormsModule, DatePipe, Pager],
  template: `
    <form class="card" (ngSubmit)="create()" #f="ngForm">
      <h2>Add a doctor</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (ok()) { <p class="ok" role="status">{{ ok() }}</p> }
      <div class="row">
        <label>Full name <input name="n" [(ngModel)]="m.fullName" required minlength="2" maxlength="100" pattern="[^<>]*" /></label>
        <label>Specialty <input name="s" [(ngModel)]="m.specialty" required minlength="2" maxlength="80" pattern="[^<>]*" /></label>
      </div>
      <div class="row">
        <label>Email <input type="email" name="e" [(ngModel)]="m.email" required maxlength="254" autocomplete="off" /></label>
        <label>Temporary password <input type="password" name="p" [(ngModel)]="m.password" required minlength="10" maxlength="128"
          pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).+" autocomplete="new-password" /></label>
      </div>
      <button [disabled]="f.invalid">Create doctor</button>
    </form>

    <form class="card" (ngSubmit)="createNurse()" #nf="ngForm">
      <h2>Add a nurse</h2>
      @if (nurseError()) { <p class="error" role="alert">{{ nurseError() }}</p> }
      @if (nurseOk()) { <p class="ok" role="status">{{ nurseOk() }}</p> }
      <div class="row">
        <label>Full name <input name="nn" [(ngModel)]="n.fullName" required minlength="2" maxlength="100" pattern="[^<>]*" /></label>
        <label>Email <input type="email" name="ne" [(ngModel)]="n.email" required maxlength="254" autocomplete="off" /></label>
      </div>
      <label>Temporary password <input type="password" name="np" [(ngModel)]="n.password" required minlength="10" maxlength="128"
        pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).+" autocomplete="new-password" />
        <small class="muted">10+ characters with upper case, lower case and a number.</small></label>
      <button [disabled]="nf.invalid">Create nurse</button>
    </form>

    <div class="card">
      <h2>Users</h2>
      <div class="toolbar">
        <input type="search" [(ngModel)]="userQ" (ngModelChange)="onUserSearch()" maxlength="100"
          placeholder="Search name or email" aria-label="Search users" />
        <select [(ngModel)]="roleFilter" (ngModelChange)="goUsers(1)" aria-label="Filter by role">
          <option value="">All roles</option>
          <option>Patient</option><option>Doctor</option><option>Nurse</option><option>Admin</option>
        </select>
      </div>
      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Role</th></tr></thead>
        <tbody>@for (u of users(); track u.id) { <tr><td>{{ u.fullName }}</td><td>{{ u.email }}</td><td>{{ u.role }}</td></tr> }</tbody>
      </table>
      @if (!users().length) { <p class="muted">No users match.</p> }
      <app-pager [page]="userPage()" [pageSize]="pageSize" [total]="userTotal()" (pageChange)="goUsers($event)" />
    </div>

    <div class="card">
      <h2>Audit log</h2>
      <div class="toolbar">
        <input type="search" [(ngModel)]="auditQ" (ngModelChange)="onAuditSearch()" maxlength="100"
          placeholder="Search action or entity (e.g. Login, CodeBlue)" aria-label="Search audit log" />
      </div>
      <table>
        <thead><tr><th>Time</th><th>Action</th><th>Entity</th><th>User</th><th>IP</th></tr></thead>
        <tbody>
          @for (a of audit(); track a.id) {
            <tr><td>{{ a.at | date: 'medium' }}</td><td>{{ a.action }}</td><td>{{ a.entity }}</td><td><small>{{ a.userId ?? '—' }}</small></td><td>{{ a.ip }}</td></tr>
          }
        </tbody>
      </table>
      @if (!audit().length) { <p class="muted">No entries match.</p> }
      <app-pager [page]="auditPage()" [pageSize]="20" [total]="auditTotal()" (pageChange)="goAudit($event)" />
    </div>
  `,
})
export class Admin implements OnInit {
  private api = inject(Api);
  pageSize = PAGE_SIZE;
  users = signal<UserRow[]>([]);
  userTotal = signal(0);
  userPage = signal(1);
  userQ = '';
  roleFilter = '';
  audit = signal<AuditRow[]>([]);
  auditTotal = signal(0);
  auditPage = signal(1);
  auditQ = '';
  private timers: Record<string, ReturnType<typeof setTimeout> | undefined> = {};
  error = signal('');
  ok = signal('');
  m = { fullName: '', specialty: '', email: '', password: '' };
  nurseError = signal('');
  nurseOk = signal('');
  n = { fullName: '', email: '', password: '' };

  ngOnInit() { this.refresh(); }

  refresh() {
    this.goUsers(this.userPage());
    this.goAudit(this.auditPage());
  }

  goUsers(p: number) {
    this.userPage.set(p);
    this.api.users({ q: this.userQ.trim(), role: this.roleFilter, page: p }).subscribe((r) => {
      this.users.set(r.items);
      this.userTotal.set(r.total);
    });
  }

  goAudit(p: number) {
    this.auditPage.set(p);
    this.api.audit({ q: this.auditQ.trim(), page: p }).subscribe((r) => {
      this.audit.set(r.items);
      this.auditTotal.set(r.total);
    });
  }

  onUserSearch() { this.debounce('u', () => this.goUsers(1)); }
  onAuditSearch() { this.debounce('a', () => this.goAudit(1)); }

  private debounce(key: string, fn: () => void) {
    clearTimeout(this.timers[key]);
    this.timers[key] = setTimeout(fn, 300);
  }

  createNurse() {
    this.nurseError.set(''); this.nurseOk.set('');
    this.api.createNurse(this.n).subscribe({
      next: (u) => { this.nurseOk.set(`Nurse ${u.fullName} created.`); this.n = { fullName: '', email: '', password: '' }; this.refresh(); },
      error: (e) => this.nurseError.set(errorText(e)),
    });
  }

  create() {
    this.error.set(''); this.ok.set('');
    this.api.createDoctor(this.m).subscribe({
      next: (d) => { this.ok.set(`Dr ${d.fullName} created.`); this.m = { fullName: '', specialty: '', email: '', password: '' }; this.refresh(); },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
