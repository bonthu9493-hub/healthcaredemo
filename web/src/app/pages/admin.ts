import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, AuditRow, UserRow, errorText } from '../core/api';

@Component({
  imports: [FormsModule, DatePipe],
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
      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Role</th></tr></thead>
        <tbody>@for (u of users(); track u.id) { <tr><td>{{ u.fullName }}</td><td>{{ u.email }}</td><td>{{ u.role }}</td></tr> }</tbody>
      </table>
    </div>

    <div class="card">
      <h2>Audit log <small class="muted">(latest 200)</small></h2>
      <table>
        <thead><tr><th>Time</th><th>Action</th><th>Entity</th><th>User</th><th>IP</th></tr></thead>
        <tbody>
          @for (a of audit(); track a.id) {
            <tr><td>{{ a.at | date: 'medium' }}</td><td>{{ a.action }}</td><td>{{ a.entity }}</td><td><small>{{ a.userId ?? '—' }}</small></td><td>{{ a.ip }}</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Admin implements OnInit {
  private api = inject(Api);
  users = signal<UserRow[]>([]);
  audit = signal<AuditRow[]>([]);
  error = signal('');
  ok = signal('');
  m = { fullName: '', specialty: '', email: '', password: '' };
  nurseError = signal('');
  nurseOk = signal('');
  n = { fullName: '', email: '', password: '' };

  ngOnInit() { this.refresh(); }

  refresh() {
    this.api.users().subscribe((u) => this.users.set(u));
    this.api.audit().subscribe((a) => this.audit.set(a));
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
