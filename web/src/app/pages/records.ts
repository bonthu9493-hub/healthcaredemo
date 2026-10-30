import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, MedicalRecord, PAGE_SIZE, PatientRow, errorText } from '../core/api';
import { Pager } from '../ui/pager';
import { Auth } from '../core/auth';

@Component({
  imports: [FormsModule, DatePipe, Pager],
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
      <div class="toolbar">
        <input type="search" [(ngModel)]="q" (ngModelChange)="onSearch()" maxlength="100"
          placeholder="Search diagnosis, prescription or patient" aria-label="Search records" />
      </div>
      @if (!items().length) { <p class="muted">{{ q ? 'No records match your search.' : 'No records.' }}</p> }
      @for (r of items(); track r.id) {
        <article class="record">
          <header><strong>{{ r.patientName }}</strong> <span class="muted">· {{ r.doctorName }} · {{ r.createdAt | date: 'medium' }}</span></header>
          <p><b>Diagnosis:</b> {{ r.diagnosis }}</p>
          @if (r.prescription) { <p><b>Prescription:</b> {{ r.prescription }}</p> }
          @if (r.notes) { <p class="muted">{{ r.notes }}</p> }
        </article>
      }
      <app-pager [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="goTo($event)" />
    </div>
  `,
})
export class Records implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  items = signal<MedicalRecord[]>([]);
  total = signal(0);
  page = signal(1);
  pageSize = PAGE_SIZE;
  q = '';
  private searchTimer?: ReturnType<typeof setTimeout>;
  patients = signal<PatientRow[]>([]);
  error = signal('');
  m = { patientId: '', diagnosis: '', prescription: '', notes: '' };

  ngOnInit() {
    this.load();
    if (this.auth.role() === 'Doctor') this.api.myPatients().subscribe((p) => this.patients.set(p));
  }

  load() {
    this.api.records({ q: this.q.trim(), page: this.page() }).subscribe((r) => {
      if (!r.items.length && r.page > 1) { this.goTo(r.page - 1); return; }
      this.items.set(r.items);
      this.total.set(r.total);
    });
  }

  goTo(p: number) { this.page.set(p); this.load(); }

  onSearch() {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.goTo(1), 300);
  }

  add() {
    this.error.set('');
    this.api.addRecord(this.m).subscribe({
      next: () => { this.m = { patientId: '', diagnosis: '', prescription: '', notes: '' }; this.load(); },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
