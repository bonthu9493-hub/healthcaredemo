import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Medicine, PAGE_SIZE, Prescription, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { money } from '../core/download';
import { Pager } from '../ui/pager';

@Component({
  imports: [FormsModule, DatePipe, Pager],
  template: `
    @if (error()) { <p class="error card" role="alert">{{ error() }}</p> }
    @if (ok()) { <p class="ok card" role="status">{{ ok() }}</p> }

    @if (auth.role() === 'Pharmacist') {
      <div class="card">
        <h2>Prescriptions to dispense</h2>
        <div class="toolbar">
          <input type="search" [(ngModel)]="rxQ" (ngModelChange)="onRxSearch()" maxlength="100" placeholder="Search patient or medicine" aria-label="Search prescriptions" />
          <select [(ngModel)]="rxStatus" (ngModelChange)="goRx(1)" aria-label="Filter by status">
            <option value="Pending">Waiting</option><option value="Dispensed">Dispensed</option><option value="Cancelled">Cancelled</option><option value="">All</option>
          </select>
        </div>
        @if (!rx().length) { <p class="muted">Nothing here.</p> }
        @for (p of rx(); track p.id) {
          <article class="record">
            <header><strong>{{ p.patientName }}</strong> <span class="muted">· {{ p.doctorName }} · {{ p.createdAt | date: 'medium' }}</span>
              <span class="badge {{ p.status === 'Pending' ? 'Scheduled' : p.status === 'Dispensed' ? 'Completed' : 'Cancelled' }}">{{ p.status }}</span></header>
            <ul>@for (i of p.items; track i.medicineId) { <li><b>{{ i.medicineName }}</b> {{ i.strength }} × {{ i.quantity }} <span class="muted">— {{ i.dosage }}, {{ i.durationDays }} days</span></li> }</ul>
            @if (p.notes) { <p class="muted">{{ p.notes }}</p> }
            @if (p.status === 'Pending') { <button class="small" (click)="dispense(p)">Dispense</button> }
          </article>
        }
        <app-pager [page]="rxPage()" [pageSize]="pageSize" [total]="rxTotal()" (pageChange)="goRx($event)" />
      </div>
    }

    @if (canEdit()) {
      <form class="card" (ngSubmit)="add()" #f="ngForm">
        <h2>Add a medicine</h2>
        <div class="row">
          <label>Name <input name="n" [(ngModel)]="m.name" required minlength="2" maxlength="100" pattern="[^<>]*" /></label>
          <label>Strength <input name="s" [(ngModel)]="m.strength" maxlength="50" pattern="[^<>]*" placeholder="e.g. 500 mg" /></label>
        </div>
        <div class="row">
          <label>Price per unit (Rs.) <input type="number" step="0.01" name="p" [(ngModel)]="m.unitPrice" required min="0.01" /></label>
          <label>Stock <input type="number" name="st" [(ngModel)]="m.stock" required min="0" /></label>
          <label>Reorder level <input type="number" name="r" [(ngModel)]="m.reorderLevel" required min="0" /></label>
          <label>Expiry date <input type="date" name="e" [(ngModel)]="m.expiryDate" required [min]="tomorrow" /></label>
        </div>
        <button [disabled]="f.invalid">Add medicine</button>
      </form>
    }

    <div class="card">
      <h2>Medicines</h2>
      <div class="toolbar">
        <input type="search" [(ngModel)]="q" (ngModelChange)="onSearch()" maxlength="100" placeholder="Search medicines" aria-label="Search medicines" />
        <label class="check"><input type="checkbox" [(ngModel)]="low" (ngModelChange)="go(1)" /> Low stock only</label>
      </div>
      @if (!meds().length) { <p class="muted">No medicines found.</p> }
      <table>
        @if (meds().length) {
          <thead><tr><th>Medicine</th><th>Price</th><th>Stock</th><th>Expiry</th>@if (canEdit()) { <th>Restock</th> }</tr></thead>
        }
        <tbody>
          @for (x of meds(); track x.id) {
            <tr>
              <td>{{ x.name }} <small class="muted">{{ x.strength }}</small></td>
              <td>Rs. {{ money(x.unitPrice) }}</td>
              <td>{{ x.stock }} @if (x.lowStock) { <span class="badge Cancelled">Low</span> }</td>
              <td>{{ x.expiryDate }} @if (x.expired) { <span class="badge Cancelled">Expired</span> } @else if (x.expiringSoon) { <span class="badge NoShow">Soon</span> }</td>
              @if (canEdit()) {
                <td class="actions">
                  <input class="qty" type="number" min="1" [(ngModel)]="qty[x.id]" [name]="'q' + x.id" placeholder="Qty" aria-label="Quantity to add" />
                  <button class="small" (click)="restock(x)" [disabled]="!qty[x.id]">Add</button>
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
      <app-pager [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="go($event)" />
    </div>
  `,
})
export class Pharmacy implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  readonly money = money;
  pageSize = PAGE_SIZE;
  tomorrow = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
  error = signal('');
  ok = signal('');
  private timers: Record<string, ReturnType<typeof setTimeout> | undefined> = {};

  meds = signal<Medicine[]>([]);
  total = signal(0);
  page = signal(1);
  q = '';
  low = false;
  qty: Record<string, number | undefined> = {};
  m = { name: '', strength: '', unitPrice: 1, stock: 0, reorderLevel: 10, expiryDate: '' };

  rx = signal<Prescription[]>([]);
  rxTotal = signal(0);
  rxPage = signal(1);
  rxQ = '';
  rxStatus = 'Pending';

  canEdit = () => this.auth.role() === 'Pharmacist' || this.auth.role() === 'Admin';

  ngOnInit() {
    this.go(1);
    if (this.auth.role() === 'Pharmacist') this.goRx(1);
  }

  go(p: number) {
    this.page.set(p);
    this.api.medicines({ q: this.q.trim(), low: this.low, page: p }).subscribe((r) => { this.meds.set(r.items); this.total.set(r.total); });
  }

  goRx(p: number) {
    this.rxPage.set(p);
    this.api.prescriptions({ q: this.rxQ.trim(), status: this.rxStatus, page: p }).subscribe((r) => { this.rx.set(r.items); this.rxTotal.set(r.total); });
  }

  onSearch() { this.debounce('m', () => this.go(1)); }
  onRxSearch() { this.debounce('r', () => this.goRx(1)); }
  private debounce(key: string, fn: () => void) { clearTimeout(this.timers[key]); this.timers[key] = setTimeout(fn, 300); }

  add() {
    this.clear();
    this.api.addMedicine(this.m).subscribe({
      next: (x) => { this.ok.set(`${x.name} added.`); this.m = { name: '', strength: '', unitPrice: 1, stock: 0, reorderLevel: 10, expiryDate: '' }; this.go(1); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  restock(x: Medicine) {
    this.clear();
    this.api.restock(x.id, this.qty[x.id]!).subscribe({
      next: (u) => { this.ok.set(`${u.name}: stock is now ${u.stock}.`); this.qty[x.id] = undefined; this.go(this.page()); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  dispense(p: Prescription) {
    this.clear();
    this.api.dispense(p.id).subscribe({
      next: () => { this.ok.set(`Dispensed to ${p.patientName}. The patient has been billed and notified.`); this.goRx(this.rxPage()); this.go(this.page()); },
      error: (e) => { this.error.set(errorText(e)); this.goRx(this.rxPage()); },
    });
  }

  private clear() { this.error.set(''); this.ok.set(''); }
}
