import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Api, Appointment, Doctor, SlotsResponse, errorText } from '../core/api';
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
            <select name="doctor" [(ngModel)]="form.doctorId" (ngModelChange)="loadSlots()" required>
              <option value="" disabled>Select…</option>
              @for (d of doctors(); track d.id) { <option [value]="d.id">{{ d.fullName }} — {{ d.specialty }}</option> }
            </select>
          </label>
          <label>Date <input type="date" name="date" [(ngModel)]="form.date" (ngModelChange)="loadSlots()" required [min]="today" /></label>
        </div>
        @if (slots().hasSchedule) {
          <div>
            <strong>Available times</strong>
            @if (!slots().slots.length) { <p class="muted">No free slots on this day. Try another date.</p> }
            <div class="slots">
              @for (s of slots().slots; track s) {
                <button type="button" class="slot" [class.picked]="form.when === s" (click)="form.when = s">{{ s | date: 'shortTime' }}</button>
              }
            </div>
          </div>
        } @else if (form.doctorId && form.date) {
          <label>Time <input type="time" name="time" [(ngModel)]="form.time" required /></label>
        }
        <label>Reason <input name="reason" [(ngModel)]="form.reason" required minlength="3" maxlength="500" pattern="[^<>]*" /></label>
        <button [disabled]="f.invalid || !canBook()">Book</button>
      </form>
    }

    <div class="card">
      <h2>{{ auth.role() === 'Admin' || auth.role() === 'Nurse' ? 'All appointments' : 'My appointments' }} <span class="live" title="Updates arrive in real time">● live</span></h2>
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
              <td><span class="badge {{ a.status }}">{{ a.status === 'NoShow' ? 'No-show' : a.status }}</span></td>
              <td class="actions">
                @if (a.status === 'Scheduled' && auth.role() !== 'Nurse') {
                  @if (auth.role() !== 'Patient') {
                    <button class="small" (click)="set(a, 'Completed')">Attended</button>
                    @if (isPast(a)) { <button class="small danger" (click)="set(a, 'NoShow')">No-show</button> }
                  }
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
  form = { doctorId: '', date: '', time: '', when: '', reason: '' };
  slots = signal<SlotsResponse>({ hasSchedule: false, slots: [] });
  today = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);

  ngOnInit() {
    this.load();
    if (this.auth.role() === 'Patient') this.api.doctors().subscribe((d) => this.doctors.set(d));
    this.rt.start();
    // The app-wide notification toast tells the user what happened; this page just refreshes its list.
    this.sub = this.rt.events.subscribe(() => {
      this.load();
      if (this.form.doctorId && this.form.date) this.loadSlots();
    });
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  isPast(a: Appointment) { return new Date(a.startsAt) <= new Date(); }

  load() { this.api.appointments().subscribe((a) => this.items.set(a)); }

  loadSlots() {
    this.form.when = '';
    this.slots.set({ hasSchedule: false, slots: [] });
    if (!this.form.doctorId || !this.form.date) return;
    this.api.slots(this.form.doctorId, this.form.date).subscribe({
      next: (s) => this.slots.set(s),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  canBook() { return this.slots().hasSchedule ? !!this.form.when : !!this.form.time; }

  book() {
    this.error.set('');
    const startsAt = this.slots().hasSchedule ? this.form.when : new Date(`${this.form.date}T${this.form.time}`).toISOString();
    this.api.book({ doctorId: this.form.doctorId, startsAt, reason: this.form.reason }).subscribe({
      next: () => {
        this.form = { doctorId: '', date: '', time: '', when: '', reason: '' };
        this.slots.set({ hasSchedule: false, slots: [] });
        this.load();
        this.notify('Appointment booked');
      },
      error: (e) => { this.error.set(errorText(e)); this.loadSlots(); },
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
