import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { tap } from 'rxjs';

export const API = 'http://localhost:5098';

export type Role = 'Patient' | 'Doctor' | 'Admin';

export interface Session {
  token: string;
  expiresAt: string;
  role: Role;
  fullName: string;
}

const KEY = 'hms.session';

function load(): Session | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(KEY) ?? 'null') as Session | null;
    return s && new Date(s.expiresAt) > new Date() ? s : null;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class Auth {
  private http = inject(HttpClient);
  private router = inject(Router);

  readonly session = signal<Session | null>(load());
  readonly role = computed(() => this.session()?.role ?? null);
  readonly loggedIn = computed(() => this.session() !== null);

  login(email: string, password: string) {
    return this.http.post<Session>(`${API}/api/auth/login`, { email, password }).pipe(
      tap((s) => {
        sessionStorage.setItem(KEY, JSON.stringify(s));
        this.session.set(s);
      }),
    );
  }

  register(body: { email: string; password: string; fullName: string; phone: string; dateOfBirth: string }) {
    return this.http.post(`${API}/api/auth/register`, body);
  }

  logout() {
    sessionStorage.removeItem(KEY);
    this.session.set(null);
    this.router.navigateByUrl('/login');
  }
}

/** Attaches the JWT to API calls only, and signs out when the server says the token is no longer valid. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.session()?.token;
  if (token && req.url.startsWith(API)) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req).pipe(
    tap({
      error: (e) => {
        if (e.status === 401 && token) auth.logout();
      },
    }),
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.loggedIn() ? true : inject(Router).parseUrl('/login');
};

export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  () => {
    const auth = inject(Auth);
    const role = auth.role();
    return role && roles.includes(role) ? true : inject(Router).parseUrl('/');
  };
