import { describe, expect, it } from 'vitest';
import { assignmentIdentity, assignPhoto, createAssignments, getCapturedPhotoUrl, isComplete, nextEmptySlot, normalizePhotoIndex, shouldInitializeAssignment, toDisplayPhotoNumber } from './assignment';

describe('customer frame assignment', () => {
  it('rejects invalid target slots and invalid completion indices', () => {
    expect(assignPhoto([0, 1], -1, 3)).toEqual([0, 1]);
    expect(assignPhoto([0, 1], 2, 3)).toEqual([0, 1]);
    expect(isComplete([0, -1])).toBe(false); expect(isComplete([0, 0])).toBe(false);
    expect(nextEmptySlot([null, 2, 3, 4], 3)).toBe(0);
  });
  it('uses null for empty slots and never photo zero', () => {
    expect(createAssignments(4)).toEqual([null, null, null, null]);
    expect(createAssignments(4, [-1, -1, -1, -1], 6)).toEqual([null, null, null, null]);
    expect(getCapturedPhotoUrl('session', null, 6)).toBeNull();
    expect(getCapturedPhotoUrl('session', -1, 6)).toBeNull();
    expect(toDisplayPhotoNumber(null)).toBeNull();
    expect(toDisplayPhotoNumber(-1)).toBeNull();
  });

  it('keeps the backend zero-based index convention explicit', () => {
    expect(normalizePhotoIndex(0, 6)).toBe(0);
    expect(toDisplayPhotoNumber(0)).toBe(1);
    expect(getCapturedPhotoUrl('session', 4, 6)).toBe('/media/sessions/session/shot/4');
    expect(toDisplayPhotoNumber(4)).toBe(5);
    expect(normalizePhotoIndex(6, 6)).toBeNull();
  });

  it('initializes once for a session and template identity', () => {
    const identity = assignmentIdentity('session-1', 'template-1');
    expect(shouldInitializeAssignment('', 'session-1', 'template-1')).toBe(true);
    expect(shouldInitializeAssignment(identity, 'session-1', 'template-1')).toBe(false);
    expect(shouldInitializeAssignment(identity, 'session-2', 'template-1')).toBe(true);
    expect(shouldInitializeAssignment(identity, 'session-1', 'template-2')).toBe(true);
  });

  it('starts at Slot 1 and advances through empty slots', () => {
    const assignments = createAssignments(4);
    const placed = assignPhoto(assignments, 0, 1);
    expect(placed).toEqual([1, null, null, null]);
    expect(nextEmptySlot(placed, 0)).toBe(1);
  });

  it('supports selecting any slot for replacement', () => {
    expect(assignPhoto([1, 4, null, null], 1, 5)).toEqual([1, 5, null, null]);
  });

  it('moves an already-used photo instead of duplicating it', () => {
    expect(assignPhoto([1, 4, null, null], 2, 1)).toEqual([null, 4, 1, null]);
  });

  it('derives the right strip from the live left assignments', () => {
    const left = assignPhoto(createAssignments(4), 0, 1);
    const updated = assignPhoto(left, 1, 4);
    expect(updated).toEqual([1, 4, null, null]);
    expect([...updated]).toEqual([1, 4, null, null]);
  });

  it('preserves prior assignments across repeated status rerenders', () => {
    let assignments = createAssignments(4);
    assignments = assignPhoto(assignments, 0, 1);
    assignments = assignPhoto(assignments, 1, 4);
    assignments = assignPhoto(assignments, 2, 2);
    expect(assignments).toEqual([1, 4, 2, null]);
  });

  it('requires every logical slot before composing', () => {
    expect(isComplete([1, 4, null, 5])).toBe(false);
    expect(isComplete([1, 4, 2, 5])).toBe(true);
  });

  it.each([2, 3, 4])('supports selectionCount %i', count => {
    expect(createAssignments(count)).toHaveLength(count);
  });
});
