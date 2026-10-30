import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { Api, BoardDoctor, PatientLookup, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { Realtime } from '../core/realtime';

@Component({
  imports: [FormsModule],
  template: `
    @if (auth.role() === 'Nurse' || auth.role() === 'Admin') {
      <form class="card" (ngSubmit)="issue()" #f="ngForm">
        <h2>Issue a token</h2>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <div class="row">
          <label>Patient (search by name)
            <input name="q" [(ngModel)]="query" (ngModelChange)="search()" maxlength="50" autocomplete="off" placeholder="Type at least 2 letters" />
          </label>
          <label>Doctor
            <select name="doc" [(ngModel)]="doctorId" required>
              <option value="" disabled>Select…</option>
              @for (d of board(); track d.doctorId) { <option [value]="d.doctorId">{{ d.doctorName }} — {{ d.specialty }}</option> }
            </select>
          </label>
        </div>
        <div class="chips">
          @for (p of found(); track p.id) {
            <button type="button" class="chip" [class.mine]="picked()?.id === p.id" (click)="picked.set(p)">{{ p.fullName }}</button>
          }
        </div>
        <button [disabled]="f.invalid || !picked()">Issue token{{ picked() ? ' for ' + picked()!.fullName : '' }}</button>
      </form>
    }

    @if (auth.role() === 'Doctor') {
      <div class="card">
        <h2>Call the next patient</h2>
        <p class="muted">Finishes the current patient and calls the next token in your queue.</p>
        <button (click)="next()" [disabled]="busy()">Call next</button>
        @if (called()) { <p class="ok" role="status">{{ called() }}</p> }
      </div>
    }

    <div class="boards">
      @for (d of board(); track d.doctorId) {
        <div class="card">
          <h2>{{ d.doctorName }} <small class="muted">({{ d.specialty }})</small></h2>
          <div><small class="muted">NOW SERVING</small></div>
          <div class="serving">{{ d.nowServing ?? '—' }}</div>
          <div><small class="muted">WAITING ({{ d.waiting.length }})</small></div>
          @if (!d.waiting.length) { <p class="muted">No one waiting.</p> }
          <div class="chips">
            @for (w of d.waiting; track w.id) {
              <span class="chip" [class.mine]="w.mine">
                #{{ w.number }}@if (w.patientName) { · {{ w.patientName }} }@if (w.mine && auth.role() === 'Patient') { · you }
                @if (auth.role() === 'Nurse' || auth.role() === 'Admin') {
                  <button type="button" class="link" (click)="skip(w.id)" title="Skip this token">✕</button>
                }
              </span>
            }
          </div>
        </div>
      }
    </div>
    @if (!board().length) { <p class="muted">No doctors yet.</p> }
  `,
})
export class Queue implements OnInit, OnDestroy {
  auth = inject(Auth);
  private api = inject(Api);
  private rt = inject(Realtime);
  private sub?: Subscription;

  board = signal<BoardDoctor[]>([]);
  found = signal<PatientLookup[]>([]);
  picked = signal<PatientLookup | null>(null);
  error = signal('');
  called = signal('');
  busy = signal(false);
  query = '';
  doctorId = '';

  ngOnInit() {
    this.load();
    this.sub = this.rt.queueChanged.subscribe(() => this.load());
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  load() { this.api.board().subscribe((b) => this.board.set(b)); }

  search() {
    const q = this.query.trim();
    if (q.length < 2) { this.found.set([]); return; }
    this.api.lookupPatients(q).subscribe((p) => this.found.set(p));
  }

  issue() {
    const p = this.picked();
    if (!p) return;
    this.error.set('');
    this.api.issueToken(p.id, this.doctorId).subscribe({
      next: () => { this.picked.set(null); this.query = ''; this.found.set([]); this.load(); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  next() {
    this.busy.set(true);
    this.api.callNext().subscribe({
      next: (r) => { this.busy.set(false); this.called.set(r.nowServing ? `Now serving token #${r.nowServing}.` : 'The queue is empty.'); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  skip(id: string) { this.api.skipToken(id).subscribe(() => this.load()); }
}
