import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Api, Appointment, Doctor, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { Realtime } from '../core/realtime';

@Component({
  imports: [FormsModule, DatePipe],
  template: `
    @if (toast()) { <div class="toast" role="status">{{ toast() }}</div> }

    @if (auth.role() === 'Patient') {
      <form class="card" (ngSubmit)="book()" #f="ngForm">
        <h2>Book an appointment</h2>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <div class="row">
          <label>Doctor
            <select name="doctor" [(ngModel)]="form.doctorId" required>
              <option value="" disabled>Select…</option>
              @for (d of doctors(); track d.id) { <option [value]="d.id">{{ d.fullName }} — {{ d.specialty }}</option> }
            </select>
          </label>
          <label>Date &amp; time <input type="datetime-local" name="when" [(ngModel)]="form.when" required [min]="minDate" /></label>
        </div>
        <label>Reason <input name="reason" [(ngModel)]="form.reason" required minlength="3" maxlength="500" pattern="[^<>]*" /></label>
        <button [disabled]="f.invalid">Book</button>
      </form>
    }

    <div class="card">
      <h2>{{ auth.role() === 'Admin' ? 'All appointments' : 'My appointments' }} <span class="live" title="Updates arrive in real time">● live</span></h2>
      @if (!items().length) { <p class="muted">No appointments yet.</p> }
      <table>
        @if (items().length) {
          <thead><tr><th>When</th><th>Patient</th><th>Doctor</th><th>Reason</th><th>Status</th><th></th></tr></thead>
        }
        <tbody>
          @for (a of items(); track a.id) {
            <tr>
              <td>{{ a.startsAt | date: 'medium' }}</td>
              <td>{{ a.patientName }}</td>
              <td>{{ a.doctorName }} <small class="muted">({{ a.specialty }})</small></td>
              <td>{{ a.reason }}</td>
              <td><span class="badge {{ a.status }}">{{ a.status }}</span></td>
              <td class="actions">
                @if (a.status === 'Scheduled') {
                  @if (auth.role() !== 'Patient') { <button class="small" (click)="set(a, 'Completed')">Complete</button> }
                  <button class="small danger" (click)="set(a, 'Cancelled')">Cancel</button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Appointments implements OnInit, OnDestroy {
  auth = inject(Auth);
  private api = inject(Api);
  private rt = inject(Realtime);
  private sub?: Subscription;

  items = signal<Appointment[]>([]);
  doctors = signal<Doctor[]>([]);
  error = signal('');
  toast = signal('');
  form = { doctorId: '', when: '', reason: '' };
  minDate = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16);

  ngOnInit() {
    this.load();
    if (this.auth.role() === 'Patient') this.api.doctors().subscribe((d) => this.doctors.set(d));
    this.rt.start();
    this.sub = this.rt.events.subscribe((e) => {
      this.load();
      const who = this.auth.role() === 'Patient' ? e.appointment.doctorName : e.appointment.patientName;
      this.notify(e.type === 'created' ? `New appointment from ${who}` : `Appointment with ${who} is now ${e.appointment.status}`);
    });
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  load() { this.api.appointments().subscribe((a) => this.items.set(a)); }

  book() {
    this.error.set('');
    this.api.book({ doctorId: this.form.doctorId, startsAt: new Date(this.form.when).toISOString(), reason: this.form.reason }).subscribe({
      next: () => { this.form = { doctorId: '', when: '', reason: '' }; this.load(); this.notify('Appointment booked'); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  set(a: Appointment, status: string) {
    this.api.setStatus(a.id, status).subscribe({ next: () => this.load(), error: (e) => this.notify(errorText(e)) });
  }

  private notify(msg: string) {
    this.toast.set(msg);
    setTimeout(() => this.toast.set(''), 4000);
  }
}
