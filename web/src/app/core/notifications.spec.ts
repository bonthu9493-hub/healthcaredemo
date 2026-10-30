import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Api, NotificationItem } from './api';
import { Notifications } from './notifications';
import { Realtime } from './realtime';

const note = (over: Partial<NotificationItem> = {}): NotificationItem => ({
  id: crypto.randomUUID(), type: 'Appointment', message: 'Hello', createdAt: new Date().toISOString(), read: false, ...over,
});

describe('Notifications', () => {
  let live: Subject<NotificationItem>;
  let api: { notifications: ReturnType<typeof vi.fn>; markRead: ReturnType<typeof vi.fn>; markAllRead: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    vi.useFakeTimers();
    live = new Subject<NotificationItem>();
    api = { notifications: vi.fn(), markRead: vi.fn(() => of(null)), markAllRead: vi.fn(() => of(null)) };
    TestBed.configureTestingModule({
      providers: [{ provide: Api, useValue: api }, { provide: Realtime, useValue: { notifications: live } }],
    });
  });

  afterEach(() => vi.useRealTimers());

  it('loads saved notifications and the unread count from the server', () => {
    api.notifications.mockReturnValue(of({ items: [note({ read: true }), note()], unread: 1 }));
    const svc = TestBed.inject(Notifications);

    svc.load();

    expect(svc.items().length).toBe(2);
    expect(svc.unreadCount()).toBe(1);
  });

  it('adds a live notification to the top, counts it, and flashes a toast that fades', () => {
    const svc = TestBed.inject(Notifications);

    live.next(note({ message: 'New appointment' }));

    expect(svc.items()[0].message).toBe('New appointment');
    expect(svc.unreadCount()).toBe(1);
    expect(svc.toast()).toBe('New appointment');
    vi.advanceTimersByTime(6100);
    expect(svc.toast()).toBe('');
  });

  it('keeps a code-blue alert on screen until acknowledged, without a toast', () => {
    const svc = TestBed.inject(Notifications);

    live.next(note({ type: 'CodeBlue', message: 'CODE BLUE at ICU' }));

    expect(svc.emergency()?.message).toBe('CODE BLUE at ICU');
    expect(svc.toast()).toBe('');
    vi.advanceTimersByTime(60_000);
    expect(svc.emergency()).not.toBeNull();
    svc.dismissEmergency();
    expect(svc.emergency()).toBeNull();
  });

  it('marks one notification read and lowers the count', () => {
    const svc = TestBed.inject(Notifications);
    const n = note();
    live.next(n);

    svc.markRead(svc.items()[0]);

    expect(api.markRead).toHaveBeenCalledWith(n.id);
    expect(svc.items()[0].read).toBe(true);
    expect(svc.unreadCount()).toBe(0);
  });

  it('does not call the server for something already read', () => {
    const svc = TestBed.inject(Notifications);
    svc.markRead(note({ read: true }));
    expect(api.markRead).not.toHaveBeenCalled();
  });

  it('marks all read, and clear() forgets everything on sign-out', () => {
    const svc = TestBed.inject(Notifications);
    live.next(note());
    live.next(note({ type: 'CodeBlue' }));

    svc.markAll();
    expect(svc.unreadCount()).toBe(0);
    expect(svc.items().every((i) => i.read)).toBe(true);

    svc.clear();
    expect(svc.items()).toEqual([]);
    expect(svc.emergency()).toBeNull();
  });
});
