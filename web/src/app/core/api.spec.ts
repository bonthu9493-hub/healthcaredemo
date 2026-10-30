import { describe, expect, it } from 'vitest';
import { errorText, query } from './api';

describe('query()', () => {
  it('leaves out empty and undefined values', () => {
    expect(query({ q: '', status: undefined, page: 1 })).toEqual({ page: '1' });
  });

  it('turns numbers into strings and keeps zero', () => {
    expect(query({ page: 2, pageSize: 10, offset: 0 })).toEqual({ page: '2', pageSize: '10', offset: '0' });
  });
});

describe('errorText()', () => {
  it('shows a friendly message when rate limited', () => {
    expect(errorText({ status: 429 })).toContain('Too many requests');
  });

  it('uses the { error } message the API sends', () => {
    expect(errorText({ status: 409, error: { error: 'That time slot is not available.' } })).toBe('That time slot is not available.');
  });

  it('joins validation errors', () => {
    const e = { status: 400, error: { errors: { Password: ['Too short.'], Email: ['Invalid.'] } } };
    expect(errorText(e)).toBe('Too short. Invalid.');
  });

  it('falls back to a generic message and never exposes raw server text', () => {
    expect(errorText({ status: 500, error: '<html>stack trace at Foo.Bar()</html>' })).toBe('Something went wrong. Please try again.');
    expect(errorText(null)).toBe('Something went wrong. Please try again.');
  });
});
