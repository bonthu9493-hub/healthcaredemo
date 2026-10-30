import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Pager } from './pager';

@Component({
  imports: [Pager],
  template: `<app-pager [page]="page()" [pageSize]="10" [total]="total()" (pageChange)="picked.push($event); page.set($event)" />`,
})
class Host {
  page = signal(1);
  total = signal(25);
  picked: number[] = [];
}

function mount(total = 25, page = 1) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.total.set(total);
  fixture.componentInstance.page.set(page);
  fixture.detectChanges();
  const el: HTMLElement = fixture.nativeElement;
  const buttons = () => Array.from(el.querySelectorAll('button')) as HTMLButtonElement[];
  return { fixture, el, host: fixture.componentInstance, prev: () => buttons()[0], next: () => buttons()[1] };
}

describe('Pager', () => {
  it('shows nothing when there are no results', () => {
    expect(mount(0).el.querySelector('nav')).toBeNull();
  });

  it('shows the page number, page count and total', () => {
    expect(mount(25).el.textContent).toContain('Page 1 of 3 · 25 results');
  });

  it('says "1 result" in the singular', () => {
    expect(mount(1).el.textContent).toContain('1 result');
    expect(mount(1).el.textContent).not.toContain('1 results');
  });

  it('disables Previous on the first page and Next on the last', () => {
    const first = mount(25, 1);
    expect(first.prev().disabled).toBe(true);
    expect(first.next().disabled).toBe(false);

    const last = mount(25, 3);
    expect(last.next().disabled).toBe(true);
    expect(last.prev().disabled).toBe(false);
  });

  it('emits the neighbouring page number when clicked', () => {
    const { fixture, host, next, prev } = mount(25, 2);

    next().click();
    fixture.detectChanges();
    prev().click();
    fixture.detectChanges();

    expect(host.picked).toEqual([3, 2]);
  });
});
