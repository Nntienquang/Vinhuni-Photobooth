import type { BoothState, Session, Template } from '@booth/shared';
import { createAssignments, type SlotAssignment } from './assignment';

export const captureCount = (template: Template) => template.captureCount && template.captureCount > 0 ? template.captureCount : template.shotsRequired;
export const selectionCount = (template: Template) => template.selectionCount && template.selectionCount > 0 ? template.selectionCount : template.shotsRequired;
export function customerPhase(state?: BoothState) {
  switch (state) {
    case 'FRAME_SELECTION': return 'TEMPLATE';
    case 'LIVE_VIEW': case 'READY': case 'COUNTDOWN': case 'CAPTURING': case 'BETWEEN_SHOTS': return 'CAPTURE';
    case 'PHOTO_SELECTION': case 'ASSIGN': case 'FILTER': return 'ASSIGN';

    case 'COMPOSING': return 'COMPOSE';
    case 'FINAL_PREVIEW': return 'CONFIRM';
    default: return state;
  }
}
export type AssignmentDraft = { identity: string; assignments: SlotAssignment[]; active: number };
const draftKey = 'vinhuni.assignment.v1';
export function loadAssignment(session: Session, memory?: AssignmentDraft | null): AssignmentDraft {
  const identity = `${session.id}:${session.template.id}:${session.retakes}`;
  const required = selectionCount(session.template);
  let cached = memory;
  try { cached ??= JSON.parse(sessionStorage.getItem(draftKey) ?? 'null'); } catch { /* Storage can be disabled. */ }
  if (cached?.identity === identity && Array.isArray(cached.assignments) && cached.assignments.length === required) {
    const assignments = createAssignments(required, cached.assignments.map(p => p ?? -1), session.shots.length);
    return { identity, assignments, active: Number.isInteger(cached.active) ? Math.max(0, Math.min(required - 1, cached.active)) : 0 };
  }
  return { identity, assignments: createAssignments(required, session.selectedShots, session.shots.length), active: 0 };
}
export function saveAssignment(draft: AssignmentDraft) {
  try { sessionStorage.setItem(draftKey, JSON.stringify(draft)); } catch { /* In-memory draft still works. */ }
}
