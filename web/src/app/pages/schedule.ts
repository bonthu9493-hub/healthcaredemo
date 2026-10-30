import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, ScheduleEntry, errorText } from '../core/api';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

@Component({
  imports: [FormsModule],
  template: `
    <form class="card" (ngSubmit)="save()" #f="ngForm">
      <h2>My weekly schedule</h2>
      <p class="muted">Patients can only book the slots generated from these windows. With no schedule, patients may pick any time.</p>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (ok()) { <p class="ok" role="status">{{ ok() }}</p> }
      @for (e of rows(); track e; let i = $index) {
        <div class="row">
          <label>Day
            <select [name]="'d' + i" [(ngModel)]="e.day">
              @for (n of days; track $index) { <option [ngValue]="$index">{{ n }}</option> }
            </select>
          </label>
          <label>From <input type="time" [name]="'s' + i" [(ngModel)]="e.start" required /></label>
          <label>To <input type="time" [name]="'e' + i" [(ngModel)]="e.end" required /></label>
          <label>Slot length
            <select [name]="'m' + i" [(ngModel)]="e.slotMinutes">
              @for (m of lengths; track m) { <option [ngValue]="m">{{ m }} min</option> }
            </select>
          </label>
          <button type="button" class="small danger" (click)="remove(i)" aria-label="Remove window">Remove</button>
        </div>
      }
      <div class="row-btns">
        <button type="button" class="secondary" (click)="add()">+ Add a time window</button>
        <button [disabled]="f.invalid">Save schedule</button>
      </div>
    </form>
  `,
})
export class Schedule implements OnInit {
  private api = inject(Api);
  days = DAYS;
  lengths = [10, 15, 20, 30, 45, 60];
  // A signal (not a plain field) so the page redraws when the rows arrive from the server.
  rows = signal<ScheduleEntry[]>([]);
  error = signal('');
  ok = signal('');

  ngOnInit() { this.api.mySchedule().subscribe((s) => this.rows.set(s)); }

  add() { this.rows.update((r) => [...r, { day: 1, start: '10:00', end: '13:00', slotMinutes: 30 }]); }
  remove(i: number) { this.rows.update((r) => r.filter((_, idx) => idx !== i)); }

  save() {
    this.error.set('');
    this.ok.set('');
    this.api.saveSchedule(this.rows()).subscribe({
      next: (s) => { this.rows.set(s); this.ok.set('Schedule saved.'); },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
