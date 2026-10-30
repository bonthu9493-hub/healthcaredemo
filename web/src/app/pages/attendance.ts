import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Api, AttendanceSummary, errorText } from '../core/api';

@Component({
  imports: [DatePipe],
  template: `
    <div class="card">
      <h2>My attendance</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (data(); as d) {
        @if (!d.today) {
          <p class="muted">You haven't checked in today.</p>
          <button (click)="checkIn()" [disabled]="busy()">Check in</button>
        } @else if (!d.today.checkOut) {
          <p>Checked in at <strong>{{ d.today.checkIn | date: 'shortTime' }}</strong>
            @if (d.today.late) { <span class="badge Cancelled">Late</span> }</p>
          <button class="danger" (click)="checkOut()" [disabled]="busy()">Check out</button>
        } @else {
          <p>Shift complete: {{ d.today.checkIn | date: 'shortTime' }} – {{ d.today.checkOut | date: 'shortTime' }}
            ({{ d.today.hours }} h)</p>
        }
      }
    </div>

    <div class="card">
      <h2>Last 45 days</h2>
      @if (!data()?.history?.length) { <p class="muted">No attendance recorded yet.</p> }
      <table>
        @if (data()?.history?.length) {
          <thead><tr><th>Date</th><th>In</th><th>Out</th><th>Hours</th><th></th></tr></thead>
        }
        <tbody>
          @for (r of data()?.history; track r.id) {
            <tr>
              <td>{{ r.date }}</td>
              <td>{{ r.checkIn | date: 'shortTime' }}</td>
              <td>{{ r.checkOut ? (r.checkOut | date: 'shortTime') : '—' }}</td>
              <td>{{ r.hours ?? '—' }}</td>
              <td>@if (r.late) { <span class="badge Cancelled">Late</span> }</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Attendance implements OnInit {
  private api = inject(Api);
  data = signal<AttendanceSummary | null>(null);
  error = signal('');
  busy = signal(false);

  ngOnInit() { this.load(); }

  load() { this.api.attendanceMe().subscribe((d) => this.data.set(d)); }

  checkIn() { this.run(this.api.checkIn()); }
  checkOut() { this.run(this.api.checkOut()); }

  private run(call: ReturnType<Api['checkIn']>) {
    this.busy.set(true);
    this.error.set('');
    call.subscribe({
      next: () => { this.busy.set(false); this.load(); },
      error: (e) => { this.busy.set(false); this.error.set(errorText(e)); this.load(); },
    });
  }
}
