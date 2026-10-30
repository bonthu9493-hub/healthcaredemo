import { Injectable, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Appointment, NotificationItem, VitalsRow } from './api';
import { API, Auth } from './auth';

export interface LiveEvent { type: 'created' | 'updated'; appointment: Appointment }

/** One shared SignalR connection. Each server event is exposed as an observable the screens can subscribe to. */
@Injectable({ providedIn: 'root' })
export class Realtime {
  private auth = inject(Auth);
  private hub?: HubConnection;
  readonly events = new Subject<LiveEvent>();
  readonly notifications = new Subject<NotificationItem>();
  readonly vitals = new Subject<VitalsRow>();
  readonly queueChanged = new Subject<void>();

  async start() {
    if (this.hub) return;
    const hub = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/notifications`, { accessTokenFactory: () => this.auth.session()?.token ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    hub.on('appointmentCreated', (a: Appointment) => this.events.next({ type: 'created', appointment: a }));
    hub.on('appointmentUpdated', (a: Appointment) => this.events.next({ type: 'updated', appointment: a }));
    hub.on('notification', (n: NotificationItem) => this.notifications.next(n));
    hub.on('vitalsRecorded', (v: VitalsRow) => this.vitals.next(v));
    hub.on('queueChanged', () => this.queueChanged.next());
    this.hub = hub;
    try {
      await hub.start();
    } catch {
      this.hub = undefined; // retried on next navigation
    }
  }

  async stop() {
    const hub = this.hub;
    this.hub = undefined;
    await hub?.stop();
  }
}
