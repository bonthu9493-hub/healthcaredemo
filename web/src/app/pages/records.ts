import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, MedicalRecord, PatientRow, errorText } from '../core/api';
import { Auth } from '../core/auth';

@Component({
  imports: [FormsModule, DatePipe],
  template: `
    @if (auth.role() === 'Doctor') {
      <form class="card" (ngSubmit)="add()" #f="ngForm">
        <h2>Add medical record</h2>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <label>Patient
          <select name="patient" [(ngModel)]="m.patientId" required>
            <option value="" disabled>Select…</option>
            @for (p of patients(); track p.id) { <option [value]="p.id">{{ p.fullName }}</option> }
          </select>
        </label>
        <label>Diagnosis <input name="dx" [(ngModel)]="m.diagnosis" required maxlength="1000" pattern="[^<>]*" /></label>
        <label>Prescription <input name="rx" [(ngModel)]="m.prescription" maxlength="1000" pattern="[^<>]*" /></label>
        <label>Notes <textarea name="notes" [(ngModel)]="m.notes" rows="3" maxlength="4000"></textarea></label>
        <button [disabled]="f.invalid">Save record</button>
      </form>
    }

    <div class="card">
      <h2>{{ auth.role() === 'Doctor' ? 'Records of my patients' : 'My medical records' }}</h2>
      @if (!items().length) { <p class="muted">No records.</p> }
      @for (r of items(); track r.id) {
        <article class="record">
          <header><strong>{{ r.patientName }}</strong> <span class="muted">· {{ r.doctorName }} · {{ r.createdAt | date: 'medium' }}</span></header>
          <p><b>Diagnosis:</b> {{ r.diagnosis }}</p>
          @if (r.prescription) { <p><b>Prescription:</b> {{ r.prescription }}</p> }
          @if (r.notes) { <p class="muted">{{ r.notes }}</p> }
        </article>
      }
    </div>
  `,
})
export class Records implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  items = signal<MedicalRecord[]>([]);
  patients = signal<PatientRow[]>([]);
  error = signal('');
  m = { patientId: '', diagnosis: '', prescription: '', notes: '' };

  ngOnInit() {
    this.load();
    if (this.auth.role() === 'Doctor') this.api.myPatients().subscribe((p) => this.patients.set(p));
  }

  load() { this.api.records().subscribe((r) => this.items.set(r)); }

  add() {
    this.error.set('');
    this.api.addRecord(this.m).subscribe({
      next: () => { this.m = { patientId: '', diagnosis: '', prescription: '', notes: '' }; this.load(); },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
