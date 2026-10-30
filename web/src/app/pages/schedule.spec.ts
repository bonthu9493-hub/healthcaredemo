import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { Api, ScheduleEntry } from '../core/api';
import { Schedule } from './schedule';

describe('Schedule page', () => {
  function mount() {
    const loaded = new Subject<ScheduleEntry[]>();
    const saved = new Subject<ScheduleEntry[]>();
    const api = { mySchedule: vi.fn(() => loaded), saveSchedule: vi.fn(() => saved) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }] });
    const fixture = TestBed.createComponent(Schedule);
    fixture.detectChanges();
    return { fixture, api, loaded, saved, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the saved windows when they arrive from the server after the page is already on screen', async () => {
    // Regression: the rows were kept in a plain field, so a late server reply never redrew the page.
    const { fixture, loaded, el } = mount();
    expect(el.querySelectorAll('input[type=time]').length).toBe(0);

    loaded.next([{ day: 1, start: '09:00', end: '12:00', slotMinutes: 30 }, { day: 3, start: '14:00', end: '17:00', slotMinutes: 20 }]);
    await fixture.whenStable();

    expect(el.querySelectorAll('input[type=time]').length).toBe(4); // a "from" and "to" for each of the two windows
  });

  it('adds and removes windows', async () => {
    const { fixture, loaded, el } = mount();
    loaded.next([]);
    await fixture.whenStable();

    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Add a time window')) as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(el.querySelectorAll('input[type=time]').length).toBe(2);

    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(el.querySelectorAll('input[type=time]').length).toBe(0);
  });

  it('sends the current windows when saving and confirms', async () => {
    const { fixture, api, loaded, saved, el } = mount();
    const rows: ScheduleEntry[] = [{ day: 2, start: '10:00', end: '12:00', slotMinutes: 30 }];
    loaded.next(rows);
    await fixture.whenStable();

    fixture.componentInstance.save();
    expect(api.saveSchedule).toHaveBeenCalledWith(rows);
    saved.next(rows);
    await fixture.whenStable();

    expect(el.textContent).toContain('Schedule saved.');
  });
});
