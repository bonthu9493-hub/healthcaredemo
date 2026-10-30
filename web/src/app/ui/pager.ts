import { Component, computed, input, output } from '@angular/core';

/** Previous / next controls for a server-paged list. */
@Component({
  selector: 'app-pager',
  template: `
    @if (total() > 0) {
      <nav class="pager" aria-label="Pagination">
        <button type="button" class="small secondary" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">‹ Previous</button>
        <span class="muted">Page {{ page() }} of {{ pages() }} · {{ total() }} result{{ total() === 1 ? '' : 's' }}</span>
        <button type="button" class="small secondary" [disabled]="page() >= pages()" (click)="pageChange.emit(page() + 1)">Next ›</button>
      </nav>
    }
  `,
})
export class Pager {
  page = input.required<number>();
  pageSize = input.required<number>();
  total = input.required<number>();
  pageChange = output<number>();
  pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
}
