import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Medicine, PAGE_SIZE, PatientRow, PatientSummary, Prescription, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { Pager } from '../ui/pager';

interface Line { medicine: Medicine; dosage: string; durationDays: number; quantity: number }

@Component({
  imports: [FormsModule, DatePipe, Pager],
  template: `
    @if (auth.role() === 'Doctor') {
      <form class="card" (ngSubmit)="submit()" #f="ngForm">
        <h2>Write a prescription</h2>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        @if (ok()) { <p class="ok" role="status">{{ ok() }}</p> }
        <label>Patient
          <select name="patient" [(ngModel)]="patientId" (ngModelChange)="onPatient()" required>
            <option value="" disabled>Select…</option>
            @for (p of patients(); track p.id) { <option [value]="p.id">{{ p.fullName }}</option> }
          </select>
        </label>
        @if (summary(); as s) {
          <p class="safety" [class.warn]="s.allergies">
            <strong>Allergies:</strong> {{ s.allergies || 'none recorded' }} ·
            <strong>Blood group:</strong> {{ s.bloodGroup || 'unknown' }} ·
            <strong>Conditions:</strong> {{ s.chronicConditions || 'none recorded' }}
          </p>
        }

        <label>Add a medicine
          <input name="mq" [(ngModel)]="mq" (ngModelChange)="searchMeds()" maxlength="100" autocomplete="off" placeholder="Type a medicine name" />
        </label>
        <div class="chips">
          @for (m of found(); track m.id) {
            <button type="button" class="chip" [disabled]="m.expired" (click)="addLine(m)" [title]="m.expired ? 'Expired' : 'Stock: ' + m.stock">
              {{ m.name }} {{ m.strength }}@if (m.expired) { (expired) }
            </button>
          }
        </div>

        @for (l of lines; track l.medicine.id; let i = $index) {
          <div class="row line">
            <strong class="who">{{ l.medicine.name }} <small class="muted">{{ l.medicine.strength }}</small></strong>
            <label>How to take <input [name]="'d' + i" [(ngModel)]="l.dosage" required minlength="2" maxlength="200" pattern="[^<>]*" placeholder="1 tablet twice daily" /></label>
            <label>Days <input type="number" [name]="'n' + i" [(ngModel)]="l.durationDays" required min="1" max="365" /></label>
            <label>Quantity <input type="number" [name]="'q' + i" [(ngModel)]="l.quantity" required min="1" max="1000" /></label>
            <button type="button" class="small danger" (click)="removeLine(i)">Remove</button>
          </div>
        }
        <label>Notes <input name="notes" [(ngModel)]="notes" maxlength="500" pattern="[^<>]*" placeholder="Optional, e.g. take after food" /></label>

        @if (allergyWarning()) {
          <label class="check warnbox"><input type="checkbox" name="ov" [(ngModel)]="override" />
            I have read the allergy warning and still want to prescribe this.</label>
        }
        <button [disabled]="f.invalid || !lines.length || !patientId || (allergyWarning() && !override)">Send to pharmacy</button>
      </form>
    }

    <div class="card">
      <h2>{{ auth.role() === 'Doctor' ? 'Prescriptions I wrote' : 'My prescriptions' }}</h2>
      <div class="toolbar">
        <input type="search" [(ngModel)]="q" (ngModelChange)="onSearch()" maxlength="100" placeholder="Search patient or medicine" aria-label="Search prescriptions" />
        <select [(ngModel)]="status" (ngModelChange)="go(1)" aria-label="Filter by status">
          <option value="">All</option><option value="Pending">Waiting at pharmacy</option><option value="Dispensed">Dispensed</option><option value="Cancelled">Cancelled</option>
        </select>
      </div>
      @if (!items().length) { <p class="muted">No prescriptions.</p> }
      @for (p of items(); track p.id) {
        <article class="record">
          <header><strong>{{ auth.role() === 'Doctor' ? p.patientName : p.doctorName }}</strong>
            <span class="muted">· {{ p.createdAt | date: 'medium' }}</span>
            <span class="badge {{ p.status === 'Pending' ? 'Scheduled' : p.status === 'Dispensed' ? 'Completed' : 'Cancelled' }}">{{ p.status === 'Pending' ? 'Waiting at pharmacy' : p.status }}</span></header>
          <ul>@for (i of p.items; track i.medicineId) { <li><b>{{ i.medicineName }}</b> {{ i.strength }} × {{ i.quantity }} <span class="muted">— {{ i.dosage }}, {{ i.durationDays }} days</span></li> }</ul>
          @if (p.notes) { <p class="muted">{{ p.notes }}</p> }
          @if (auth.role() === 'Doctor' && p.status === 'Pending') { <button class="small danger" (click)="cancel(p)">Cancel</button> }
        </article>
      }
      <app-pager [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="go($event)" />
    </div>
  `,
})
export class Prescriptions implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  pageSize = PAGE_SIZE;
  private timers: Record<string, ReturnType<typeof setTimeout> | undefined> = {};

  items = signal<Prescription[]>([]);
  total = signal(0);
  page = signal(1);
  q = '';
  status = '';

  patients = signal<PatientRow[]>([]);
  summary = signal<PatientSummary | null>(null);
  found = signal<Medicine[]>([]);
  allergyWarning = signal(false);
  error = signal('');
  ok = signal('');
  patientId = '';
  mq = '';
  notes = '';
  override = false;
  lines: Line[] = [];

  ngOnInit() {
    this.go(1);
    if (this.auth.role() === 'Doctor') this.api.myPatients().subscribe((p) => this.patients.set(p));
  }

  go(p: number) {
    this.page.set(p);
    this.api.prescriptions({ q: this.q.trim(), status: this.status, page: p }).subscribe((r) => { this.items.set(r.items); this.total.set(r.total); });
  }

  onSearch() { clearTimeout(this.timers['s']); this.timers['s'] = setTimeout(() => this.go(1), 300); }

  /** Shows the patient's allergies right where the doctor is choosing medicines. */
  onPatient() {
    this.summary.set(null);
    this.allergyWarning.set(false);
    this.override = false;
    if (this.patientId) this.api.patientSummary(this.patientId).subscribe((s) => this.summary.set(s));
  }

  searchMeds() {
    clearTimeout(this.timers['m']);
    const term = this.mq.trim();
    if (term.length < 2) { this.found.set([]); return; }
    this.timers['m'] = setTimeout(() => this.api.medicines({ q: term, page: 1, pageSize: 8 }).subscribe((r) => this.found.set(r.items)), 250);
  }

  addLine(m: Medicine) {
    if (this.lines.some((l) => l.medicine.id === m.id)) return;
    this.lines = [...this.lines, { medicine: m, dosage: '', durationDays: 5, quantity: 10 }];
    this.mq = '';
    this.found.set([]);
  }

  removeLine(i: number) { this.lines = this.lines.filter((_, idx) => idx !== i); this.allergyWarning.set(false); this.override = false; }

  submit() {
    this.error.set('');
    this.ok.set('');
    this.api.prescribe({
      patientId: this.patientId, notes: this.notes, overrideAllergyWarning: this.override,
      items: this.lines.map((l) => ({ medicineId: l.medicine.id, dosage: l.dosage, durationDays: l.durationDays, quantity: l.quantity })),
    }).subscribe({
      next: () => {
        this.ok.set('Prescription sent to the pharmacy.');
        this.lines = []; this.notes = ''; this.override = false; this.allergyWarning.set(false);
        this.go(1);
      },
      error: (e) => {
        this.allergyWarning.set(e?.error?.allergyConflict === true);
        this.error.set(errorText(e));
      },
    });
  }

  cancel(p: Prescription) { this.api.cancelPrescription(p.id).subscribe(() => this.go(this.page())); }
}
