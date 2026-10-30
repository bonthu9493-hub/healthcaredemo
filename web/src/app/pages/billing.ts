import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Invoice, PAGE_SIZE, errorText } from '../core/api';
import { Auth } from '../core/auth';
import { money, saveBlob } from '../core/download';
import { Pager } from '../ui/pager';

@Component({
  imports: [FormsModule, DatePipe, Pager],
  template: `
    <div class="card">
      <h2>{{ auth.role() === 'Patient' ? 'My bills' : 'Invoices' }}</h2>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (ok()) { <p class="ok" role="status">{{ ok() }}</p> }
      <div class="toolbar">
        @if (auth.role() !== 'Patient') {
          <input type="search" [(ngModel)]="q" (ngModelChange)="onSearch()" maxlength="100" placeholder="Search by patient name" aria-label="Search invoices" />
        } @else { <span></span> }
        <select [(ngModel)]="status" (ngModelChange)="go(1)" aria-label="Filter by status">
          <option value="">All</option><option value="Open">Unpaid</option><option value="Paid">Paid</option><option value="Void">Void</option>
        </select>
      </div>
      @if (!items().length) { <p class="muted">No invoices.</p> }

      @for (i of items(); track i.id) {
        <article class="record">
          <header class="inv-head">
            <button type="button" class="link" (click)="toggle(i)" [attr.aria-expanded]="openId() === i.id">{{ i.number }}</button>
            @if (auth.role() !== 'Patient') { <strong>{{ i.patientName }}</strong> }
            <span class="muted">{{ i.createdAt | date: 'mediumDate' }}</span>
            <span class="badge {{ i.status === 'Paid' ? 'Completed' : i.status === 'Open' ? 'Scheduled' : 'Cancelled' }}">{{ i.status === 'Open' ? 'Unpaid' : i.status }}</span>
            <strong class="amount">Rs. {{ money(i.total) }}</strong>
            <button type="button" class="small secondary" (click)="download(i)">{{ i.status === 'Paid' ? 'Receipt (PDF)' : 'Invoice (PDF)' }}</button>
          </header>

          @if (openId() === i.id) {
            <table>
              <thead><tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr></thead>
              <tbody>
                @for (x of i.items; track x.id) {
                  <tr><td>{{ x.description }} <small class="muted">{{ x.source }}</small></td><td>{{ x.quantity }}</td><td>{{ money(x.unitPrice) }}</td><td>{{ money(x.total) }}</td></tr>
                }
              </tbody>
            </table>
            @if (i.status === 'Paid') { <p class="muted">Paid {{ i.paidAt | date: 'medium' }} by {{ i.paymentMethod }}.</p> }

            @if (i.status === 'Open' && auth.role() !== 'Patient') {
              <div class="row line">
                <label>Extra charge <input [name]="'d' + i.id" [(ngModel)]="extra.description" maxlength="200" pattern="[^<>]*" placeholder="e.g. Wound dressing" /></label>
                <label>Qty <input type="number" [name]="'q' + i.id" [(ngModel)]="extra.quantity" min="1" /></label>
                <label>Price <input type="number" step="0.01" [name]="'p' + i.id" [(ngModel)]="extra.unitPrice" min="0.01" /></label>
                <button type="button" class="small" (click)="addItem(i)" [disabled]="extra.description.trim().length < 2 || !extra.unitPrice">Add</button>
              </div>
              <div class="row line">
                <label>Payment method
                  <select [name]="'m' + i.id" [(ngModel)]="method"><option>Cash</option><option>Card</option><option>UPI</option></select>
                </label>
                <button type="button" (click)="pay(i)" [disabled]="!i.items.length">Mark as paid</button>
                @if (auth.role() === 'Admin') { <button type="button" class="danger" (click)="voidIt(i)">Void invoice</button> }
              </div>
            }
          }
        </article>
      }
      <app-pager [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="go($event)" />
    </div>
  `,
})
export class Billing implements OnInit {
  auth = inject(Auth);
  private api = inject(Api);
  readonly money = money;
  pageSize = PAGE_SIZE;
  private timer?: ReturnType<typeof setTimeout>;

  items = signal<Invoice[]>([]);
  total = signal(0);
  page = signal(1);
  openId = signal<string | null>(null);
  error = signal('');
  ok = signal('');
  q = '';
  status = '';
  method = 'Cash';
  extra = { description: '', quantity: 1, unitPrice: 0 };

  ngOnInit() { this.go(1); }

  go(p: number) {
    this.page.set(p);
    this.api.invoices({ q: this.q.trim(), status: this.status, page: p }).subscribe((r) => { this.items.set(r.items); this.total.set(r.total); });
  }

  onSearch() { clearTimeout(this.timer); this.timer = setTimeout(() => this.go(1), 300); }

  toggle(i: Invoice) { this.openId.set(this.openId() === i.id ? null : i.id); this.clear(); }

  addItem(i: Invoice) {
    this.clear();
    this.api.addInvoiceItem(i.id, this.extra).subscribe({
      next: () => { this.extra = { description: '', quantity: 1, unitPrice: 0 }; this.go(this.page()); },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  pay(i: Invoice) {
    this.clear();
    this.api.payInvoice(i.id, this.method).subscribe({
      next: (p) => { this.ok.set(`${p.number} marked as paid (${this.method}). The patient has been notified.`); this.go(this.page()); },
      error: (e) => { this.error.set(errorText(e)); this.go(this.page()); },
    });
  }

  voidIt(i: Invoice) {
    this.clear();
    this.api.voidInvoice(i.id).subscribe({ next: () => { this.ok.set(`${i.number} voided.`); this.go(this.page()); }, error: (e) => this.error.set(errorText(e)) });
  }

  download(i: Invoice) {
    this.api.invoicePdf(i.id).subscribe({
      next: (blob) => saveBlob(blob, `${i.status === 'Paid' ? 'receipt' : 'invoice'}-${i.number}.pdf`),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  private clear() { this.error.set(''); this.ok.set(''); }
}
