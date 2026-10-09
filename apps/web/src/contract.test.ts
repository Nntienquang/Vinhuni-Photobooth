import { describe, expect, it } from 'vitest';
import type { Slot, Template } from '@booth/shared';
describe('shared pixel-coordinate contract', () => {
  it('preserves independent photo index for duplicated slots through JSON', () => {
    const slot: Slot = { id: 'one', x: 80, y: 180, width: 500, height: 600, rotation: 12, borderRadius: 15, shotIndex: 0, focalX: .3, focalY: .7 };
    const template: Template = { id: 'frame', name: 'Frame', outputWidth: 1200, outputHeight: 1800, shotsRequired: 1, dpi: 300, enabled: true, order: 0, orientation: 'portrait', slots: [slot, { ...slot, id: 'two', x: 620 }] };
    const result = JSON.parse(JSON.stringify(template)) as Template;
    expect(result.slots[1].shotIndex).toBe(0); expect(result.slots[1].x + result.slots[1].width).toBeLessThanOrEqual(result.outputWidth);
  });
});
