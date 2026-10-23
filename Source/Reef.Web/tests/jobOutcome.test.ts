import { describe, expect, it } from 'vitest';
import { jobOutcome } from '../src/lib/jobOutcome';

const run = (status: string | number, outputData: string | null = null) => ({ id: 1, jobId: 1, startedAt: '2026-10-23T10:00:00Z', status, outputData });

describe('jobOutcome', () => {
  it.each([
    ['Running', 'running'],
    ['Scheduled', 'running'],
    [2, 'running'],
    ['Completed', 'success'],
    [3, 'success'],
    ['Failed', 'failed'],
    ['Cancelled', 'failed'],
    [4, 'failed'],
  ])('status %s is %s', (status, expected) => {
    expect(jobOutcome(run(status))).toBe(expected);
  });

  it('marks an import with failed rows as partial', () => {
    expect(jobOutcome(run('Completed', '{"executionId":7,"status":"PartialSuccess"}'))).toBe('partial');
  });

  it('marks an export with failed split files as partial', () => {
    expect(jobOutcome(run('Completed', '{"executionId":7}'), { status: 'Success', splitFailureCount: 2 })).toBe('partial');
  });

  it('keeps a clean export a success', () => {
    expect(jobOutcome(run('Completed', '{"executionId":7}'), { status: 'Success', splitFailureCount: 0 })).toBe('success');
  });
});
