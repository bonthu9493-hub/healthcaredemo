import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Admission, Api, PatientLookup, Ward, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { money } from '../core/download';
import { Realtime } from '../core/realtime';

@Component({
  imports: [FormsModule, DatePipe],
  template: `
    @if (error()) { <p class="error card" role="alert">{{ error() }}</p> }
    @if (ok()) { <p class="ok card" role="status">{{ ok() }}</p> }

    @if (auth.role() === 'Patient') {
      <div class="card">
        <h2>My hospital stays</h2>
        @if (!mine().length) { <p class="muted">You have not been admitted.</p> }
        @for (a of mine(); track a.id) {
          <article class="record">
            <header><strong>{{ a.wardName }} · {{ a.bedLabel }}</strong>
              <span class="muted">· admitted {{ a.admittedAt | date: 'medium' }}</span>
              <span class="badge {{ a.dischargedAt ? 'Completed' : 'Scheduled' }}">{{ a.dischargedAt ? 'Discharged' : 'In hospital' }}</span></header>
            <p>{{ a.reason }}</p>
            @if (a.dischargeSummary) { <p class="result"><b>Discharge summary:</b> {{ a.dischargeSummary }}</p> }
          </article>
        }
      </div>
    } @else {
      @if (auth.role() === 'Admin') {
        <div class="row">
          <form class="card" (ngSubmit)="addWard()" #wf="ngForm">
            <h2>Add a ward</h2>
            <label>Name <input name="wn" [(ngModel)]="w.name" required minlength="2" maxlength="60" pattern="[^<>]*" /></label>
            <div class="row">
              <label>Type <select name="wt" [(ngModel)]="w.type"><option>General</option><option>ICU</option><option>Private</option><option>Maternity</option><option>Emergency</option></select></label>
              <label>Rate per day (Rs.) <input type="number" name="wr" [(ngModel)]="w.dailyRate" required min="0" /></label>
            </div>
            <button [disabled]="wf.invalid">Add ward</button>
          </form>
          <form class="card" (ngSubmit)="addBed()" #bf="ngForm">
            <h2>Add a bed</h2>
            <label>Ward <select name="bw" [(ngModel)]="b.wardId" required><option value="" disabled>Select…</option>@for (x of wards(); track x.id) { <option [value]="x.id">{{ x.name }}</option> }</select></label>
            <label>Bed label <input name="bl" [(ngModel)]="b.label" required maxlength="30" pattern="[^<>]*" placeholder="e.g. Bed 4" /></label>
            <button [disabled]="bf.invalid">Add bed</button>
          </form>
        </div>
      }

      @if (auth.role() === 'Doctor' || auth.role() === 'Nurse') {
        <form class="card" (ngSubmit)="admit()" #af="ngForm">
          <h2>Admit a patient</h2>
          @if (auth.role() === 'Nurse') {
            <label>Find patient <input name="aq" [(ngModel)]="query" (ngModelChange)="search()" maxlength="50" autocomplete="off" placeholder="Type at least 2 letters" /></label>
            <div class="chips">@for (p of found(); track p.id) { <button type="button" class="chip" [class.mine]="patientId === p.id" (click)="patientId = p.id">{{ p.fullName }}</button> }</div>
          } @else {
            <label>Patient <select name="ap" [(ngModel)]="patientId" required><option value="" disabled>Select…</option>@for (p of myPatients(); track p.id) { <option [value]="p.id">{{ p.fullName }}</option> }</select></label>
          }
          <div class="row">
            <label>Bed <select name="ab" [(ngModel)]="bedId" required><option value="" disabled>Select…</option>@for (f of freeBeds(); track f.id) { <option [value]="f.id">{{ f.ward }} — {{ f.label }}</option> }</select></label>
            <label>Reason <input name="ar" [(ngModel)]="reason" required minlength="3" maxlength="300" pattern="[^<>]*" /></label>
          </div>
          <button [disabled]="af.invalid || !patientId">Admit</button>
        </form>
      }

      <div class="boards">
        @for (w of wards(); track w.id) {
          <div class="card">
            <h2>{{ w.name }} <small class="muted">({{ w.type }} · Rs. {{ money(w.dailyRate) }}/day)</small></h2>
            <p class="muted">{{ w.occupied }} of {{ w.totalBeds }} beds occupied</p>
            @if (!w.beds.length) { <p class="muted">No beds yet.</p> }
            @for (bed of w.beds; track bed.id) {
              <div class="bed" [class.occupied]="bed.admissionId">
                <strong>{{ bed.label }}</strong>
                @if (bed.admissionId) {
                  <span>{{ bed.patientName }} <small class="muted">since {{ bed.admittedAt | date: 'd MMM, h:mm a' }}</small></span>
                  @if (auth.role() === 'Doctor') {
                    @if (dischargeFor() === bed.admissionId) {
                      <input class="grow" [(ngModel)]="summary" [name]="'s' + bed.id" maxlength="2000" pattern="[^<>]*" placeholder="Discharge summary (required)" aria-label="Discharge summary" />
                      <button class="small" (click)="discharge(bed.admissionId)" [disabled]="summary.trim().length < 5">Confirm</button>
                      <button class="small secondary" (click)="dischargeFor.set(null)">Cancel</button>
                    } @else { <button class="small" (click)="dischargeFor.set(bed.admissionId); summary = ''">Discharge</button> }
                  }
                } @else { <span class="free">Free</span> }
              </div>
            }
          </div>
        }
      </div>
      @if (!wards().length) { <p class="muted">No wards have been set up yet.</p> }
    }
  `,
})
export class Wards implements OnInit, OnDestroy {
  auth = inject(Auth);
  private api = inject(Api);
  private rt = inject(Realtime);
  private sub?: Subscription;
  readonly money = money;

  wards = signal<Ward[]>([]);
  mine = signal<Admission[]>([]);
  myPatients = signal<{ id: string; fullName: string }[]>([]);
  found = signal<PatientLookup[]>([]);
  dischargeFor = signal<string | null>(null);
  error = signal('');
  ok = signal('');
  freeBeds = computed(() => this.wards().flatMap((w) => w.beds.filter((b) => !b.admissionId).map((b) => ({ id: b.id, label: b.label, ward: w.name }))));

  w = { name: '', type: 'General', dailyRate: 1000 };
  b = { wardId: '', label: '' };
  query = '';
  patientId = '';
  bedId = '';
  reason = '';
  summary = '';

  ngOnInit() {
    this.load();
    if (this.auth.role() === 'Doctor') this.api.myPatients().subscribe((p) => this.myPatients.set(p));
    this.sub = this.rt.bedsChanged.subscribe(() => this.load());
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  load() {
    if (this.auth.role() === 'Patient') this.api.myAdmissions().subscribe((a) => this.mine.set(a));
    else this.api.wards().subscribe((w) => this.wards.set(w));
  }

  search() {
    const q = this.query.trim();
    if (q.length < 2) { this.found.set([]); return; }
    this.api.lookupPatients(q).subscribe((p) => this.found.set(p));
  }

  addWard() {
    this.clear();
    this.api.createWard(this.w).subscribe({ next: (x) => { this.ok.set(`${x.name} added.`); this.w = { name: '', type: 'General', dailyRate: 1000 }; this.load(); }, error: (e) => this.error.set(errorText(e)) });
  }

  addBed() {
    this.clear();
    this.api.addBed(this.b.wardId, this.b.label).subscribe({ next: () => { this.ok.set('Bed added.'); this.b.label = ''; this.load(); }, error: (e) => this.error.set(errorText(e)) });
  }

  admit() {
    this.clear();
    this.api.admit({ patientId: this.patientId, bedId: this.bedId, reason: this.reason }).subscribe({
      next: (a) => { this.ok.set(`Admitted to ${a.wardName}, ${a.bedLabel}.`); this.patientId = ''; this.bedId = ''; this.reason = ''; this.found.set([]); this.query = ''; this.load(); },
      error: (e) => { this.error.set(errorText(e)); this.load(); },
    });
  }

  discharge(admissionId: string) {
    this.clear();
    this.api.discharge(admissionId, this.summary.trim()).subscribe({
      next: () => { this.ok.set('Patient discharged. The bed charge was added to their invoice.'); this.dischargeFor.set(null); this.load(); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  private clear() { this.error.set(''); this.ok.set(''); }
}
