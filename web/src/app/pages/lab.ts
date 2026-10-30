import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, LabOrder, LabTest, PAGE_SIZE, PatientRow, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { money } from '../core/download';
import { Pager } from '../ui/pager';

@Component({
  imports: [FormsModule, DatePipe, Pager],
  template: `
    @if (error()) { <p class="error card" role="alert">{{ error() }}</p> }
    @if (ok()) { <p class="ok card" role="status">{{ ok() }}</p> }

    @if (auth.role() === 'Doctor') {
      <form class="card" (ngSubmit)="order()" #f="ngForm">
        <h2>Order a lab test</h2>
        <div class="row">
          <label>Patient
            <select name="p" [(ngModel)]="patientId" required>
              <option value="" disabled>Select…</option>
              @for (p of patients(); track p.id) { <option [value]="p.id">{{ p.fullName }}</option> }
            </select>
          </label>
          <label>Test
            <select name="t" [(ngModel)]="testId" required>
              <option value="" disabled>Select…</option>
              @for (t of tests(); track t.id) { <option [value]="t.id">{{ t.name }} — Rs. {{ money(t.price) }}</option> }
            </select>
          </label>
        </div>
        <label>Notes <input name="n" [(ngModel)]="notes" maxlength="500" pattern="[^<>]*" placeholder="Optional, e.g. fasting sample" /></label>
        <button [disabled]="f.invalid">Order test</button>
      </form>
    }

    <div class="card">
      <h2>{{ auth.role() === 'LabTech' ? 'Lab orders' : auth.role() === 'Doctor' ? 'Tests I ordered' : 'My lab results' }}</h2>
      <div class="toolbar">
        <input type="search" [(ngModel)]="q" (ngModelChange)="onSearch()" maxlength="100" placeholder="Search test or patient" aria-label="Search lab orders" />
        <select [(ngModel)]="status" (ngModelChange)="go(1)" aria-label="Filter by status">
          <option value="">All</option><option value="Ordered">Waiting for result</option><option value="Completed">Completed</option><option value="Cancelled">Cancelled</option>
        </select>
      </div>
      @if (!items().length) { <p class="muted">No lab orders.</p> }
      @for (o of items(); track o.id) {
        <article class="record">
          <header>
            <strong>{{ o.testName }}</strong>
            <span class="muted">· {{ auth.role() === 'Patient' ? o.doctorName : o.patientName }} · {{ o.orderedAt | date: 'medium' }}</span>
            <span class="badge {{ o.status === 'Ordered' ? 'Scheduled' : o.status === 'Completed' ? 'Completed' : 'Cancelled' }}">{{ o.status === 'Ordered' ? 'Waiting' : o.status }}</span>
            @if (o.abnormal) { <span class="badge Critical">Abnormal</span> }
          </header>
          @if (o.notes) { <p class="muted">{{ o.notes }}</p> }
          @if (o.resultText) { <p class="result"><b>Result:</b> {{ o.resultText }}</p> }
          @if (auth.role() === 'LabTech' && o.status === 'Ordered') {
            <div class="row">
              <label>Result <textarea [name]="'r' + o.id" [(ngModel)]="drafts[o.id]" rows="2" maxlength="2000"></textarea></label>
            </div>
            <label class="check"><input type="checkbox" [name]="'a' + o.id" [(ngModel)]="abnormal[o.id]" /> Result is abnormal</label>
            <button class="small" (click)="save(o)" [disabled]="(drafts[o.id] ?? '').trim().length < 2">Save result</button>
          }
          @if (auth.role() === 'Doctor' && o.status === 'Ordered') { <button class="small danger" (click)="cancel(o)">Cancel</button> }
        </article>
      }
      <app-pager [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="go($event)" />
    </div>
  `,
})
export class Lab implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  readonly money = money;
  pageSize = PAGE_SIZE;
  private timer?: ReturnType<typeof setTimeout>;

  items = signal<LabOrder[]>([]);
  total = signal(0);
  page = signal(1);
  patients = signal<PatientRow[]>([]);
  tests = signal<LabTest[]>([]);
  error = signal('');
  ok = signal('');
  q = '';
  status = '';
  patientId = '';
  testId = '';
  notes = '';
  drafts: Record<string, string | undefined> = {};
  abnormal: Record<string, boolean | undefined> = {};

  ngOnInit() {
    if (this.auth.role() === 'LabTech') this.status = 'Ordered';
    this.go(1);
    if (this.auth.role() === 'Doctor') {
      this.api.myPatients().subscribe((p) => this.patients.set(p));
      this.api.labTests().subscribe((t) => this.tests.set(t));
    }
  }

  go(p: number) {
    this.page.set(p);
    this.api.labOrders({ q: this.q.trim(), status: this.status, page: p }).subscribe((r) => { this.items.set(r.items); this.total.set(r.total); });
  }

  onSearch() { clearTimeout(this.timer); this.timer = setTimeout(() => this.go(1), 300); }

  order() {
    this.clear();
    this.api.orderLab({ patientId: this.patientId, labTestId: this.testId, notes: this.notes }).subscribe({
      next: (o) => { this.ok.set(`${o.testName} ordered. The lab has been notified.`); this.notes = ''; this.go(1); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  save(o: LabOrder) {
    this.clear();
    this.api.labResult(o.id, { resultText: (this.drafts[o.id] ?? '').trim(), abnormal: !!this.abnormal[o.id] }).subscribe({
      next: () => { this.ok.set('Result saved. The doctor and patient have been notified and the test billed.'); this.go(this.page()); },
      error: (e) => { this.error.set(errorText(e)); this.go(this.page()); },
    });
  }

  cancel(o: LabOrder) { this.api.cancelLab(o.id).subscribe(() => this.go(this.page())); }

  private clear() { this.error.set(''); this.ok.set(''); }
}
