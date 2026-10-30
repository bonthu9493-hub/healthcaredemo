import { Injectable, inject, signal } from '@angular/core';
import { Api, NotificationItem } from './api';
import { Realtime } from './realtime';

/** Saved notifications plus the live ones pushed by the server. */
@Injectable({ providedIn: 'root' })
export class Notifications {
  private api = inject(Api);
  private rt = inject(Realtime);

  readonly items = signal<NotificationItem[]>([]);
  readonly unreadCount = signal(0);
  readonly toast = signal('');
  /** The latest code-blue alert, kept on screen until acknowledged. */
  readonly emergency = signal<NotificationItem | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.rt.notifications.subscribe((n) => {
      this.items.update((list) => [n, ...list].slice(0, 50));
      this.unreadCount.update((c) => c + 1);
      if (n.type === 'CodeBlue') this.emergency.set(n);
      else this.flash(n.message);
    });
  }

  load() {
    this.api.notifications().subscribe((r) => {
      this.items.set(r.items);
      this.unreadCount.set(r.unread);
    });
  }

  clear() {
    this.items.set([]);
    this.unreadCount.set(0);
    this.emergency.set(null);
    this.toast.set('');
  }

  markRead(n: NotificationItem) {
    if (n.read) return;
    this.api.markRead(n.id).subscribe(() => {
      this.items.update((list) => list.map((x) => (x.id === n.id ? { ...x, read: true } : x)));
      this.unreadCount.update((c) => Math.max(0, c - 1));
    });
  }

  markAll() {
    this.api.markAllRead().subscribe(() => {
      this.items.update((list) => list.map((x) => ({ ...x, read: true })));
      this.unreadCount.set(0);
    });
  }

  dismissEmergency() { this.emergency.set(null); }

  flash(message: string) {
    this.toast.set(message);
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(''), 6000);
  }
}
