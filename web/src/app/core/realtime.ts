import { Injectable, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Appointment } from './api';
import { API, Auth } from './auth';

export interface LiveEvent { type: 'created' | 'updated'; appointment: Appointment }

@Injectable({ providedIn: 'root' })
export class Realtime {
  private auth = inject(Auth);
  private hub?: HubConnection;
  readonly events = new Subject<LiveEvent>();

  async start() {
    if (this.hub) return;
    this.hub = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/notifications`, { accessTokenFactory: () => this.auth.session()?.token ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    this.hub.on('appointmentCreated', (a: Appointment) => this.events.next({ type: 'created', appointment: a }));
    this.hub.on('appointmentUpdated', (a: Appointment) => this.events.next({ type: 'updated', appointment: a }));
    try {
      await this.hub.start();
    } catch {
      this.hub = undefined; // retried on next navigation
    }
  }

  async stop() {
    await this.hub?.stop();
    this.hub = undefined;
  }
}
