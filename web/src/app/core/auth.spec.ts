import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { API, Auth, Session, authGuard, authInterceptor, homeFor, roleGuard } from './auth';

const KEY = 'hms.session';
const future = () => new Date(Date.now() + 30 * 60_000).toISOString();
const session = (over: Partial<Session> = {}): Session => ({ token: 'tok', expiresAt: future(), role: 'Patient', fullName: 'Pat', ...over });

function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  return { auth: TestBed.inject(Auth), http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
}

describe('Auth', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('starts signed out when nothing is stored', () => {
    const { auth } = setup();
    expect(auth.loggedIn()).toBe(false);
    expect(auth.role()).toBeNull();
  });

  it('restores a stored session that has not expired', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session({ role: 'Doctor' })));
    const { auth } = setup();
    expect(auth.loggedIn()).toBe(true);
    expect(auth.role()).toBe('Doctor');
  });

  it('ignores an expired stored session', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session({ expiresAt: new Date(Date.now() - 1000).toISOString() })));
    expect(setup().auth.loggedIn()).toBe(false);
  });

  it('ignores a corrupted stored session instead of crashing', () => {
    sessionStorage.setItem(KEY, '{not json');
    expect(setup().auth.loggedIn()).toBe(false);
  });

  it('login posts the credentials and keeps the session', () => {
    const { auth, ctl } = setup();
    auth.login('a@b.c', 'Secret12345').subscribe();

    const req = ctl.expectOne(`${API}/api/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.c', password: 'Secret12345' });
    req.flush(session({ role: 'Nurse' }));

    expect(auth.role()).toBe('Nurse');
    expect(JSON.parse(sessionStorage.getItem(KEY)!).token).toBe('tok');
  });

  it('logout clears the session and goes to the login page', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session()));
    const { auth, router } = setup();
    const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    auth.logout();

    expect(auth.loggedIn()).toBe(false);
    expect(sessionStorage.getItem(KEY)).toBeNull();
    expect(nav).toHaveBeenCalledWith('/login');
  });
});

describe('authInterceptor', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('adds the bearer token to calls to our API', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session()));
    const { http, ctl } = setup();
    http.get(`${API}/api/doctors`).subscribe();
    const req = ctl.expectOne(`${API}/api/doctors`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok');
    req.flush([]);
  });

  it('never sends the token to any other site', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session()));
    const { http, ctl } = setup();
    http.get('https://evil.example.com/steal').subscribe();
    const req = ctl.expectOne('https://evil.example.com/steal');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('signs the user out when the server answers 401', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session()));
    const { auth, http, ctl, router } = setup();
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    http.get(`${API}/api/doctors`).subscribe({ error: () => undefined });
    ctl.expectOne(`${API}/api/doctors`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.loggedIn()).toBe(false);
  });

  it('keeps the session on other errors such as 403', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session()));
    const { auth, http, ctl } = setup();

    http.get(`${API}/api/admin/users`).subscribe({ error: () => undefined });
    ctl.expectOne(`${API}/api/admin/users`).flush({}, { status: 403, statusText: 'Forbidden' });

    expect(auth.loggedIn()).toBe(true);
  });
});

describe('route guards', () => {
  beforeEach(() => sessionStorage.clear());

  const run = (guard: ReturnType<typeof roleGuard>) =>
    TestBed.runInInjectionContext(() => guard({} as never, {} as never));

  it('authGuard lets a signed-in user through and sends others to /login', () => {
    setup();
    expect(String(TestBed.runInInjectionContext(() => authGuard({} as never, {} as never)))).toBe('/login');

    sessionStorage.setItem(KEY, JSON.stringify(session()));
    TestBed.resetTestingModule();
    setup();
    expect(TestBed.runInInjectionContext(() => authGuard({} as never, {} as never))).toBe(true);
  });

  it('roleGuard allows only the listed roles', () => {
    sessionStorage.setItem(KEY, JSON.stringify(session({ role: 'Patient' })));
    setup();
    expect(run(roleGuard('Patient', 'Doctor'))).toBe(true);
    expect(String(run(roleGuard('Admin')))).toBe('/');
  });

  it('roleGuard sends someone who is signed out to the login page (not in a loop through "/")', () => {
    setup();
    expect(String(run(roleGuard('Patient')))).toBe('/login');
  });

  it('every role has its own landing page', () => {
    expect(homeFor('Pharmacist')).toBe('/pharmacy');
    expect(homeFor('LabTech')).toBe('/lab');
    expect(homeFor('Patient')).toBe('/appointments');
    expect(homeFor('Admin')).toBe('/appointments');
    expect(homeFor(null)).toBe('/appointments');
  });
});
