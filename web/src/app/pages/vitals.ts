import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Api, PatientLookup, VitalsRow, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { Realtime } from '../core/realtime';

@Component({
  imports: [FormsModule, DatePipe],
  template: `
    @if (auth.role() !== 'Patient') {
      <div class="card">
        <h2>Choose a patient</h2>
        @if (auth.role() === 'Nurse') {
          <label>Search by name
            <input name="q" [(ngModel)]="query" (ngModelChange)="search()" maxlength="50" placeholder="Type at least 2 letters" autocomplete="off" />
          </label>
        } @else {
          @if (!patients().length) { <p class="muted">You have no patients yet (patients appear after booking with you).</p> }
        }
        <div class="chips">
          @for (p of patients(); track p.id) {
            <button class="chip" [class.mine]="selected()?.id === p.id" (click)="select(p)">{{ p.fullName }}</button>
          }
        </div>
      </div>

      @if (selected(); as p) {
        <form class="card" (ngSubmit)="record()" #f="ngForm">
          <h2>Record vitals for {{ p.fullName }}</h2>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          @if (ok()) { <p [class]="ok().startsWith('Saved') ? 'ok' : 'error'" role="status">{{ ok() }}</p> }
          <div class="grid5">
            <label>Heart rate (bpm) <input type="number" name="hr" [(ngModel)]="m.heartRate" required min="20" max="250" /></label>
            <label>Systolic (mmHg) <input type="number" name="sys" [(ngModel)]="m.systolic" required min="50" max="260" /></label>
            <label>Diastolic (mmHg) <input type="number" name="dia" [(ngModel)]="m.diastolic" required min="30" max="160" /></label>
            <label>Oxygen SpO₂ (%) <input type="number" name="spo2" [(ngModel)]="m.spO2" required min="50" max="100" /></label>
            <label>Temperature (°C) <input type="number" step="0.1" name="t" [(ngModel)]="m.temperatureC" required min="30" max="45" /></label>
          </div>
          <button [disabled]="f.invalid">Save reading</button>
        </form>
      }
    }

    @if (auth.role() === 'Patient' || selected()) {
      <div class="card">
        <h2>{{ auth.role() === 'Patient' ? 'My vitals' : 'Vitals history' }} <span class="live" title="New readings appear instantly">● live</span></h2>
        @if (!rows().length) { <p class="muted">No readings yet.</p> }
        @if (spark()) {
          <p class="muted">Heart rate trend (last {{ Math.min(rows().length, 20) }} readings)</p>
          <svg class="spark" viewBox="0 0 200 40" preserveAspectRatio="none" role="img" aria-label="Heart rate trend">
            <polyline [attr.points]="spark()" />
          </svg>
        }
        <table>
          @if (rows().length) {
            <thead><tr><th>When</th><th>HR</th><th>BP</th><th>SpO₂</th><th>Temp</th><th>Status</th><th>By</th></tr></thead>
          }
          <tbody>
            @for (r of rows(); track r.id) {
              <tr [class]="r.severity">
                <td>{{ r.recordedAt | date: 'medium' }}</td>
                <td>{{ r.heartRate }}</td>
                <td>{{ r.systolic }}/{{ r.diastolic }}</td>
                <td>{{ r.spO2 }}%</td>
                <td>{{ r.temperatureC }}°C</td>
                <td><span class="badge {{ r.severity }}">{{ r.severity }}</span>
                  @if (r.alertReason) { <br /><small class="muted">{{ r.alertReason }}</small> }</td>
                <td>{{ r.recordedByName }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class Vitals implements OnInit, OnDestroy {
  auth = inject(Auth);
  private api = inject(Api);
  private rt = inject(Realtime);
  private sub?: Subscription;
  readonly Math = Math;

  patients = signal<PatientLookup[]>([]);
  selected = signal<PatientLookup | null>(null);
  rows = signal<VitalsRow[]>([]);
  error = signal('');
  ok = signal('');
  query = '';
  m = { heartRate: 72, systolic: 120, diastolic: 80, spO2: 98, temperatureC: 36.8 };

  spark = computed(() => {
    const v = [...this.rows()].reverse().slice(-20).map((r) => r.heartRate);
    if (v.length < 2) return '';
    const min = Math.min(...v);
    const span = Math.max(Math.max(...v) - min, 1);
    return v.map((x, i) => `${(i / (v.length - 1)) * 200},${36 - ((x - min) / span) * 32}`).join(' ');
  });

  ngOnInit() {
    if (this.auth.role() === 'Patient') this.load();
    if (this.auth.role() === 'Doctor') this.api.myPatients().subscribe((p) => this.patients.set(p.map((x) => ({ id: x.id, fullName: x.fullName }))));
    this.sub = this.rt.vitals.subscribe((v) => {
      if (this.auth.role() === 'Patient' || v.patientId === this.selected()?.id) this.load();
    });
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  search() {
    const q = this.query.trim();
    if (q.length < 2) { this.patients.set([]); return; }
    this.api.lookupPatients(q).subscribe((p) => this.patients.set(p));
  }

  select(p: PatientLookup) {
    this.selected.set(p);
    this.ok.set('');
    this.error.set('');
    this.load();
  }

  load() {
    this.api.vitals(this.selected()?.id).subscribe((r) => this.rows.set(r));
  }

  record() {
    const p = this.selected();
    if (!p) return;
    this.error.set('');
    this.api.recordVitals({ patientId: p.id, ...this.m }).subscribe({
      next: (r) => {
        this.ok.set(r.severity === 'Normal' ? 'Saved. Readings are normal.' : `Saved. ${r.severity.toUpperCase()} — ${r.alertReason}. Staff were alerted.`);
        this.load();
      },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
