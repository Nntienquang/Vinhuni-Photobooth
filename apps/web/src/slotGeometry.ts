import type { Slot, Template } from '@booth/shared';

export function displaySlots(template: Template) {
  const source = template.slots.map((slot, sourceIndex) => ({ slot, sourceIndex, duplicate: false }));
  if (!template.duplicateStrip) return source;
  const offset = template.duplicateOffsetX ?? template.outputWidth / 2;
  const mirror = template.duplicateMode === 'mirror';
  return [...source, ...source.map(item => ({ ...item, duplicate: true, slot: { ...item.slot,
    id: `${item.slot.id}-right`, x: offset + (mirror ? template.outputWidth / 2 - item.slot.x - item.slot.width : item.slot.x),
    y: item.slot.y + (template.duplicateOffsetY ?? 0), rotation: mirror ? -item.slot.rotation : item.slot.rotation,
  } }))];
}

// Same source rectangle as PhotoComposer.Cover; CSS object-position uses a different formula.
export function coverCrop(width: number, height: number, targetWidth: number, targetHeight: number, focalX: number, focalY: number) {
  const scale = Math.max(targetWidth / width, targetHeight / height);
  const w = targetWidth / scale, h = targetHeight / scale;
  return { x: Math.max(0, Math.min(width - w, width * focalX - w / 2)), y: Math.max(0, Math.min(height - h, height * focalY - h / 2)), width: w, height: h };
}
export function slotStyle(slot: Slot, template: Template) {
  return { left: `${slot.x / template.outputWidth * 100}%`, top: `${slot.y / template.outputHeight * 100}%`,
    width: `${slot.width / template.outputWidth * 100}%`, height: `${slot.height / template.outputHeight * 100}%`,
    transform: `rotate(${slot.rotation}deg)`, borderRadius: `${slot.borderRadius / slot.width * 100}% / ${slot.borderRadius / slot.height * 100}%` };
}

export type Handle = 'move' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';
export function dragSlot(source: Slot, dx: number, dy: number, handle: Handle): Slot {
  if (handle === 'move') return { ...source, x: source.x + dx, y: source.y + dy };
  const angle = source.rotation * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
  const localX = dx * c + dy * s, localY = -dx * s + dy * c;
  const width = Math.max(10, source.width + (handle.includes('e') ? localX : handle.includes('w') ? -localX : 0));
  const height = Math.max(10, source.height + (handle.includes('s') ? localY : handle.includes('n') ? -localY : 0));
  const cx = (width - source.width) / 2 * (handle.includes('w') ? -1 : 1);
  const cy = (height - source.height) / 2 * (handle.includes('n') ? -1 : 1);
  return { ...source, width, height, x: source.x + source.width / 2 + cx * c - cy * s - width / 2,
    y: source.y + source.height / 2 + cx * s + cy * c - height / 2 };
}

export function snapSlot(slot: Slot, template: Template, edges: boolean, center: boolean, tolerance = 12): Slot {
  const nearest = (values: number[]) => values.sort((a, b) => Math.abs(a) - Math.abs(b)).find(value => Math.abs(value) <= tolerance) ?? 0;
  const xs = edges ? [-slot.x, template.outputWidth - slot.x - slot.width] : [];
  const ys = edges ? [-slot.y, template.outputHeight - slot.y - slot.height] : [];
  if (center) { xs.push(template.outputWidth / 2 - slot.x - slot.width / 2); ys.push(template.outputHeight / 2 - slot.y - slot.height / 2); }
  return { ...slot, x: slot.x + nearest(xs), y: slot.y + nearest(ys) };
}
