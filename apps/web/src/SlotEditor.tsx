import { useRef, useState } from 'react';
import type { Slot, Template } from '@booth/shared';
import { displaySlots, dragSlot, slotStyle, snapSlot, type Handle } from './slotGeometry';
import { SlotPhoto } from './SlotPhoto';

const handles: Handle[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];
export function SlotEditor({ template, selected, select, update, overlay, preview }: {
  template: Template; selected: number; select: (index: number) => void; update: (slot: Partial<Slot>) => void; overlay: string; preview: boolean;
}) {
  const [edges, setEdges] = useState(true), [center, setCenter] = useState(true), [safe, setSafe] = useState(true);
  const [bounds, setBounds] = useState(true), [crop, setCrop] = useState(true), [labels, setLabels] = useState(true);
  const [opacity, setOpacity] = useState(1), [zoom, setZoom] = useState(1);
  const canvas = useRef<HTMLDivElement>(null);
  const drag = useRef<{ x: number; y: number; slot: Slot; handle: Handle } | null>(null);
  const slots = displaySlots(template).sort((a, b) => (a.slot.order ?? 0) - (b.slot.order ?? 0));
  return <>
    <div className="editor-guide-toolbar">
      {([['Snap to edges', edges, setEdges], ['Snap to center', center, setCenter], ['Safe margin', safe, setSafe], ['Show slot bounds', bounds, setBounds], ['Show crop preview', crop, setCrop], ['Show labels', labels, setLabels]] as const).map(([name, value, setter]) => <label className="toggle" key={name}><span>{name}</span><input type="checkbox" checked={value} onChange={e => setter(e.target.checked)} /></label>)}
      <label>Frame opacity <input aria-label="Frame opacity" type="range" min="0" max="1" step=".05" value={opacity} onChange={e => setOpacity(Number(e.target.value))} /></label>
      <label>Zoom <select aria-label="Editor zoom" value={zoom} onChange={e => setZoom(Number(e.target.value))}>{[.75, 1, 1.5, 2, 3].map(n => <option key={n} value={n}>{n * 100}%</option>)}</select></label>
      <span title="The uploaded PNG never moves or resizes when dragging slots">🔒 Frame overlay locked</span>
    </div>
    <div className="slot-editor-viewport"><div className="slot-editor-sheet" ref={canvas} tabIndex={0} aria-label="Visual slot canvas"
      style={{ width: `${390 * zoom}px`, aspectRatio: `${template.outputWidth}/${template.outputHeight}` }}
      onKeyDown={e => {
        if (preview || !template.slots[selected]) return;
        const delta = e.shiftKey ? 10 : 1;
        const dx = e.key === 'ArrowLeft' ? -delta : e.key === 'ArrowRight' ? delta : 0;
        const dy = e.key === 'ArrowUp' ? -delta : e.key === 'ArrowDown' ? delta : 0;
        if (dx || dy) { e.preventDefault(); update({ x: template.slots[selected].x + dx, y: template.slots[selected].y + dy }); }
      }} onPointerMove={e => {
        if (!drag.current || !canvas.current || preview) return;
        const scale = template.outputWidth / canvas.current.getBoundingClientRect().width;
        let slot = dragSlot(drag.current.slot, (e.clientX - drag.current.x) * scale, (e.clientY - drag.current.y) * scale, drag.current.handle);
        if (drag.current.handle === 'move') slot = snapSlot(slot, template, edges, center, 6 * scale);
        update({ ...slot, x: Math.round(slot.x * 100) / 100, y: Math.round(slot.y * 100) / 100, width: Math.round(slot.width * 100) / 100, height: Math.round(slot.height * 100) / 100 });
      }} onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }}>
      {slots.map(({ slot }) => <div className="slot-editor-image" key={slot.id} style={slotStyle(slot, template)}>{(crop || preview) && <SlotPhoto src="/sample-photo.svg" slot={slot} alt="Sample landscape photo, cover crop" />}</div>)}
      <img className="slot-editor-overlay" src={overlay} alt="Frame PNG overlay" draggable={false} style={{ opacity: preview ? 1 : opacity }} />
      {!preview && <>
        {center && <><div className="slot-guide center-x" /><div className="slot-guide center-y" /></>}
        {safe && <div className="slot-safe-margin" style={{ inset: `${60 / template.outputHeight * 100}% ${60 / template.outputWidth * 100}%` }} />}
        {slots.map(({ slot, sourceIndex, duplicate }) => <div key={slot.id} data-slot-id={slot.id} data-source-index={sourceIndex}
          className={`slot-editor-hit ${bounds ? 'show-bounds' : ''} ${selected === sourceIndex ? 'selected' : ''} ${duplicate ? 'derived' : ''}`}
          style={slotStyle(slot, template)} onPointerDown={e => {
            if (duplicate) return;
            select(sourceIndex); canvas.current?.focus();
            drag.current = { x: e.clientX, y: e.clientY, slot: { ...slot }, handle: (e.target as HTMLElement).dataset.handle as Handle || 'move' };
            e.currentTarget.setPointerCapture(e.pointerId); e.preventDefault();
          }}>
          {labels && <span className="slot-editor-label">Slot {sourceIndex + 1} · Photo {slot.shotIndex + 1}{duplicate ? ' · derived' : ''}</span>}
          {!duplicate && selected === sourceIndex && handles.map(handle => <i key={handle} data-handle={handle} className={`slot-editor-handle handle-${handle}`} />)}
        </div>)}
      </>}
    </div></div>
    <p className="muted">Drag slots or their 8 handles. Arrow keys: 1 px; Shift + arrow: 10 px. Geometry uses output pixels. Preview hides all guides.</p>
  </>;
}
