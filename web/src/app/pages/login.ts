import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorText } from '../core/api';
import { Auth } from '../core/auth';

@Component({
  imports: [FormsModule, RouterLink],
  template: `
    <form class="card narrow" (ngSubmit)="submit()" #f="ngForm">
      <h2>Sign in</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      <label>Email <input type="email" name="email" [(ngModel)]="email" required maxlength="254" autocomplete="username" /></label>
      <label>Password <input type="password" name="password" [(ngModel)]="password" required maxlength="128" autocomplete="current-password" /></label>
      <button [disabled]="f.invalid || busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
      <p class="muted">New patient? <a routerLink="/register">Create an account</a></p>
    </form>
  `,
})
export class Login {
  private auth = inject(Auth);
  private router = inject(Router);
  email = '';
  password = '';
  error = signal('');
  busy = signal(false);

  submit() {
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.email, this.password).subscribe({
      next: () => this.router.navigateByUrl('/'),
      error: (e) => {
        this.busy.set(false);
        this.error.set(errorText(e));
      },
    });
  }
}
