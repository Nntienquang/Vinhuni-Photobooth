import { describe, expect, it } from 'vitest';
import { DEFAULT_FOCAL_POINT, PRINT_SHEET, SAMPLE_PHOTO, displaySlots, newTemplate } from './pages/TemplateManager';
import { coverCrop, dragSlot, slotStyle } from './slotGeometry';

describe('visual slot editor contract', () => {
  it('duplicates with explicit offsets without mutating source geometry', () => {
    const template = newTemplate(); const original = structuredClone(template.slots);
    template.slots[0] = { ...template.slots[0], x: 80, rotation: 15 };
    template.duplicateMode = 'mirror'; template.duplicateOffsetX = 610; template.duplicateOffsetY = 20;
    const slots = displaySlots(template);
    expect(slots[0].slot).toEqual(template.slots[0]);
    expect(slots[4].slot).toMatchObject({ x: 680, y: 200, rotation: -15, shotIndex: 0 });
    expect(template.slots[1]).toEqual(original[1]);
    template.duplicateStrip = false; expect(displaySlots(template)).toHaveLength(4);
  });
  it('matches the compositor focal crop instead of CSS object-position', () => {
    expect(coverCrop(2400, 1600, 500, 600, 1, 0)).toMatchObject({ x: 2400 - 1600 * 5 / 6, y: 0, height: 1600 });
    const crop = coverCrop(2400, 1600, 400, 800, .4, .5);
    expect(crop).toEqual({ x: 560, y: 0, width: 800, height: 1600 });
  });
  it('resizes rotated edges while preserving the opposite edge', () => {
    const slot = { ...newTemplate().slots[0], x: 100, y: 100, width: 200, height: 100, rotation: 90 };
    expect(dragSlot(slot, 0, 40, 'e')).toMatchObject({ x: 80, y: 120, width: 240, height: 100 });
    expect(dragSlot(slot, 10, 20, 'move')).toMatchObject({ x: 110, y: 120 });
  });
  it('scales a circular output radius equally on both axes', () => {
    const template = newTemplate(); const slot = { ...template.slots[0], width: 200, height: 100, borderRadius: 20 };
    expect(slotStyle(slot, template).borderRadius).toBe('10% / 20%');
  });
  it('uses the 10x15 portrait print sheet defaults', () => {
    const template = newTemplate();
    expect(template.outputWidth).toBe(1200);
    expect(template.outputHeight).toBe(1800);
    expect(template.dpi).toBe(300);
    expect(template.captureCount).toBe(6);
    expect(template.selectionCount).toBe(4);
    expect(template.duplicateStrip).toBe(true);
    expect(PRINT_SHEET.safeMargin).toBe(60);
  });

  it('creates four numbered left slots and four right duplicates', () => {
    const slots = displaySlots(newTemplate());
    expect(slots).toHaveLength(8);
    expect(slots.slice(0, 4).map(item => item.sourceIndex)).toEqual([0, 1, 2, 3]);
    expect(slots.slice(4).map(item => item.slot.x)).toEqual(slots.slice(0, 4).map(item => item.slot.x + 600));
    expect(slots.slice(4).every(item => item.duplicate)).toBe(true);
  });

  it('uses a horizontal 3:2 sample and focal-aware cover position', () => {
    expect(SAMPLE_PHOTO).toBe('/sample-photo.svg');
    expect(DEFAULT_FOCAL_POINT).toEqual({ x: .5, y: .4 });
  });
});
