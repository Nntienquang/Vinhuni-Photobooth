export type SlotAssignment = number | null;

export function normalizePhotoIndex(value: number | null | undefined, capturedCount: number): SlotAssignment {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < capturedCount ? value : null;
}

export function toDisplayPhotoNumber(photoIndex: number | null | undefined): number | null {
  return typeof photoIndex === 'number' && Number.isInteger(photoIndex) && photoIndex >= 0 ? photoIndex + 1 : null;
}

export function getCapturedPhotoUrl(sessionId: string, photoIndex: number | null | undefined, capturedCount: number): string | null {
  return normalizePhotoIndex(photoIndex, capturedCount) === null ? null : `/media/sessions/${sessionId}/shot/${photoIndex}`;
}

export function assignmentIdentity(sessionId: string, templateId: string): string {
  return `${sessionId}:${templateId}`;
}

export function shouldInitializeAssignment(initializedIdentity: string, sessionId: string, templateId: string): boolean {
  return initializedIdentity !== assignmentIdentity(sessionId, templateId);
}

export function createAssignments(selectionCount: number, selectedPhotos?: number[], capturedCount = Number.MAX_SAFE_INTEGER): SlotAssignment[] {
  return Array.from({ length: selectionCount }, (_, index) => normalizePhotoIndex(selectedPhotos?.[index], capturedCount));
}

export function assignPhoto(assignments: SlotAssignment[], slotIndex: number, photoIndex: number): SlotAssignment[] {
  if (!Number.isInteger(photoIndex) || photoIndex < 0 || !Number.isInteger(slotIndex) || slotIndex < 0 || slotIndex >= assignments.length) return [...assignments];
  const next = [...assignments];
  const previousSlot = next.indexOf(photoIndex);
  if (previousSlot >= 0) next[previousSlot] = null;
  next[slotIndex] = photoIndex;
  return next;
}

export function nextEmptySlot(assignments: SlotAssignment[], activeSlot: number): number {
  const following = assignments.findIndex((photo, index) => index > activeSlot && photo === null);
  return following >= 0 ? following : assignments.indexOf(null) >= 0 ? assignments.indexOf(null) : activeSlot;
}

export function isComplete(assignments: SlotAssignment[]): boolean {
  return assignments.length > 0 && assignments.every(photo => photo !== null && Number.isInteger(photo) && photo >= 0) && new Set(assignments).size === assignments.length;
}
