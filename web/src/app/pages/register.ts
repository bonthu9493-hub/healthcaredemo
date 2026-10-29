import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorText } from '../core/api';
import { Auth } from '../core/auth';

@Component({
  imports: [FormsModule, RouterLink],
  template: `
    <form class="card narrow" (ngSubmit)="submit()" #f="ngForm">
      <h2>Patient registration</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      <label>Full name <input name="fullName" [(ngModel)]="m.fullName" required minlength="2" maxlength="100" pattern="[^<>]*" /></label>
      <label>Email <input type="email" name="email" [(ngModel)]="m.email" required maxlength="254" autocomplete="username" /></label>
      <label>Phone <input name="phone" [(ngModel)]="m.phone" required pattern="\\+?[0-9 \\-]{7,20}" /></label>
      <label>Date of birth <input type="date" name="dob" [(ngModel)]="m.dateOfBirth" required [max]="today" /></label>
      <label>Password <input type="password" name="password" [(ngModel)]="m.password" required minlength="10" maxlength="128"
        pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).+" autocomplete="new-password" />
        <small class="muted">10+ characters with upper case, lower case and a number.</small></label>
      <button [disabled]="f.invalid || busy()">Create account</button>
      <p class="muted">Already registered? <a routerLink="/login">Sign in</a></p>
    </form>
  `,
})
export class Register {
  private auth = inject(Auth);
  private router = inject(Router);
  today = new Date().toISOString().slice(0, 10);
  m = { fullName: '', email: '', phone: '', dateOfBirth: '', password: '' };
  error = signal('');
  busy = signal(false);

  submit() {
    this.busy.set(true);
    this.error.set('');
    this.auth.register(this.m).subscribe({
      next: () => this.auth.login(this.m.email, this.m.password).subscribe(() => this.router.navigateByUrl('/')),
      error: (e) => {
        this.busy.set(false);
        this.error.set(errorText(e));
      },
    });
  }
}
