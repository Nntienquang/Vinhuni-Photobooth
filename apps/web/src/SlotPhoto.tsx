import { useState } from 'react';
import type { Slot } from '@booth/shared';
import { coverCrop } from './slotGeometry';

export function SlotPhoto({ src, slot, alt, mirror = false }: { src: string; slot: Slot; alt: string; mirror?: boolean }) {
  const [size, setSize] = useState({ width: 3, height: 2 });
  const crop = coverCrop(size.width, size.height, slot.width, slot.height, slot.focalX, slot.focalY);
  return <div className="slot-photo-clip" style={{ transform: mirror ? 'scaleX(-1)' : undefined }}><img src={src} alt={alt} draggable={false}
    onLoad={e => setSize({ width: e.currentTarget.naturalWidth, height: e.currentTarget.naturalHeight })}
    style={{ position: 'absolute', maxWidth: 'none', width: `${size.width / crop.width * 100}%`, height: `${size.height / crop.height * 100}%`, left: `${-crop.x / crop.width * 100}%`, top: `${-crop.y / crop.height * 100}%` }} /></div>;
}
