import { afterEach, expect, it, vi } from 'vitest';
import type { Session } from '@booth/shared';
import { captureCount, selectionCount, customerPhase, loadAssignment, saveAssignment } from './boothFlow';

const session = { id: 'flow', retakes: 0, shots: ['a','b','c','d','e','f'], selectedShots: [], template: { id: 'frame', captureCount: 6, selectionCount: 4, shotsRequired: 4 } } as unknown as Session;
afterEach(() => vi.unstubAllGlobals());
it('separates capture count from logical selection count', () => {
  expect(captureCount(session.template)).toBe(6);
  expect(selectionCount(session.template)).toBe(4);
});
it('routes current and legacy assignment states through frame-first assignment', () => {
  expect(['FRAME_SELECTION','CAPTURING','ASSIGN','FILTER','COMPOSING','FINAL_PREVIEW','THANK_YOU'].map(s => customerPhase(s as Session['state'])))
    .toEqual(['TEMPLATE','CAPTURE','ASSIGN','ASSIGN','COMPOSE','CONFIRM','THANK_YOU']);
  expect(customerPhase('PHOTO_SELECTION')).toBe('ASSIGN');
});
it('keeps partial slot order across status updates and reloads, isolates retakes', () => {
  const values = new Map<string,string>();
  vi.stubGlobal('sessionStorage', { getItem: (k: string) => values.get(k) ?? null, setItem: (k: string, v: string) => values.set(k,v) });
  const draft = { ...loadAssignment(session), assignments: [2,4,null,null], active: 2 };
  saveAssignment(draft);
  expect(loadAssignment(session).assignments).toEqual([2,4,null,null]);
  expect(loadAssignment(session,draft)).toEqual(draft);
  expect(loadAssignment({ ...session,retakes: 1 }).assignments).toEqual([null,null,null,null]);
  expect(loadAssignment({ ...session,id: 'next' }).active).toBe(0);
});
