import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, AppointmentReportRow, ReportKind, StaffReportRow, errorText } from '../core/api';

const iso = (d: Date) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);

@Component({
  imports: [FormsModule],
  template: `
    <div class="card">
      <h2>Reports</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      <div class="row">
        <label>From <input type="date" name="from" [(ngModel)]="from" [max]="to" /></label>
        <label>To <input type="date" name="to" [(ngModel)]="to" [min]="from" [max]="today" /></label>
      </div>
      <button (click)="run()">Run reports</button>
    </div>

    <div class="card">
      <h2>Staff attendance <small class="muted">(weekdays only; late = after 09:00 + 15 min)</small></h2>
      <button class="small" (click)="download('staff-attendance')">Download CSV</button>
      @if (!staff().length) { <p class="muted">No staff yet.</p> }
      <table>
        @if (staff().length) {
          <thead><tr><th>Name</th><th>Role</th><th>Working days</th><th>Present</th><th>Absent</th><th>Hours</th><th>Late</th><th>No check-out</th></tr></thead>
        }
        <tbody>
          @for (r of staff(); track r.userId) {
            <tr>
              <td>{{ r.fullName }}</td><td>{{ r.role }}</td><td>{{ r.workingDays }}</td><td>{{ r.daysPresent }}</td>
              <td>{{ r.daysAbsent }}</td><td>{{ r.totalHours }}</td><td>{{ r.lateCount }}</td><td>{{ r.missingCheckOut }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    <div class="card">
      <h2>Appointment attendance <small class="muted">(rate = attended ÷ (attended + no-show))</small></h2>
      <button class="small" (click)="download('appointment-attendance')">Download CSV</button>
      @if (!appts().length) { <p class="muted">No appointments in this period.</p> }
      <table>
        @if (appts().length) {
          <thead><tr><th>Doctor</th><th>Total</th><th>Attended</th><th>No-show</th><th>Cancelled</th><th>Upcoming</th><th>Not marked</th><th>Rate</th></tr></thead>
        }
        <tbody>
          @for (r of appts(); track r.doctorId) {
            <tr>
              <td>{{ r.doctorName }} <small class="muted">({{ r.specialty }})</small></td>
              <td>{{ r.total }}</td><td>{{ r.attended }}</td><td>{{ r.noShow }}</td><td>{{ r.cancelled }}</td>
              <td>{{ r.upcoming }}</td><td>{{ r.unmarked }}</td>
              <td>{{ r.attendanceRate === null ? '—' : r.attendanceRate + '%' }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Reports implements OnInit {
  private api = inject(Api);
  today = iso(new Date());
  to = this.today;
  from = iso(new Date(Date.now() - 29 * 86400000));
  staff = signal<StaffReportRow[]>([]);
  appts = signal<AppointmentReportRow[]>([]);
  error = signal('');

  ngOnInit() { this.run(); }

  run() {
    this.error.set('');
    this.api.staffReport(this.from, this.to).subscribe({ next: (r) => this.staff.set(r), error: (e) => this.error.set(errorText(e)) });
    this.api.appointmentReport(this.from, this.to).subscribe({ next: (r) => this.appts.set(r), error: (e) => this.error.set(errorText(e)) });
  }

  /** The CSV endpoint needs the login token, so fetch it through HttpClient and save the blob. */
  download(kind: ReportKind) {
    this.api.reportCsv(kind, this.from, this.to).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${kind}_${this.from}_to_${this.to}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
