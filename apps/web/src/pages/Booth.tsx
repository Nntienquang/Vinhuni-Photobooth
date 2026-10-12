import { useEffect, useRef, useState } from 'react';
import { QRCodeSVG } from 'qrcode.react';

import { ArrowRight, Camera, Check, Expand, Heart, RotateCcw, Sparkles } from 'lucide-react';

import type { Session, Template } from '@booth/shared';
import { api, useBooth } from '../api';
import { displaySlots, slotStyle } from '../slotGeometry';
import { SlotPhoto } from '../SlotPhoto';

import { assignPhoto as placePhoto, createAssignments, getCapturedPhotoUrl, isComplete, nextEmptySlot, toDisplayPhotoNumber, type SlotAssignment } from '../assignment';

import { captureCount as templateCaptureCount, selectionCount as templateSelectionCount, customerPhase, loadAssignment, saveAssignment, type AssignmentDraft } from '../boothFlow';



export function CloudQr({ session }: { session: Session }) {
  if (session.uploadStatus === 'none' || session.uploadStatus === 'disabled') return null;
  return <div className="cloud-qr" aria-live="polite" style={{ margin: '16px auto' }}>
    {session.uploadStatus === 'uploaded' && session.shareUrl ? <>
      <QRCodeSVG value={session.shareUrl} size={300} level="M" marginSize={4} bgColor="#ffffff" fgColor="#000000" title="Quét mã để tải ảnh" style={{ width: 'min(300px, 80vw)', height: 'auto', display: 'block', margin: '0 auto' }} />
      <p>Quét mã để tải ảnh</p><small>Ảnh chất lượng gốc</small>
    </> : session.uploadStatus === 'uploading' || session.uploadStatus === 'pending' ? <>
      <p>Đang tạo mã QR...</p><small>Ảnh đã được lưu và có thể in bình thường.</small>
    </> : <>
      <p>Chưa thể tạo mã QR.</p><small>Ảnh vẫn đã được lưu và in bình thường.</small>
    </>}
  </div>;
}

function FrameAssignment({ session, assignments, activeSlot, onSlotClick, mirror }: { session: Session; assignments: SlotAssignment[]; activeSlot: number; onSlotClick: (slot: number) => void; mirror: boolean }) {
  const slots = displaySlots(session.template).sort((a, b) => (a.slot.order ?? 0) - (b.slot.order ?? 0));
  return <div className="assignment-frame" style={{ aspectRatio: `${session.template.outputWidth}/${session.template.outputHeight}` }}>
    {slots.map(({ slot, duplicate }) => {
      const logicalIndex = slot.shotIndex;
      const photo = assignments[logicalIndex] ?? null;
      const originalUrl = getCapturedPhotoUrl(session.id, photo, session.shots.length);
      const url = originalUrl ? `${originalUrl}?take=${session.retakes}` : null;
      return <button key={slot.id} type="button" className={`assignment-slot ${activeSlot === logicalIndex ? 'active' : ''} ${duplicate ? 'duplicate-slot' : ''} ${url ? '' : 'empty'}`}
        disabled={duplicate} aria-label={`Slot ${logicalIndex + 1}${duplicate ? ' duplicate' : ''}`} style={slotStyle(slot, session.template)} onClick={() => onSlotClick(logicalIndex)}>
        {url ? <SlotPhoto key={url} src={url} slot={slot} mirror={mirror} alt={`Ảnh ${toDisplayPhotoNumber(photo)} trong Slot ${logicalIndex + 1}`} /> : <span>Chọn ảnh<small>Slot {logicalIndex + 1}</small></span>}
        <b>Slot {logicalIndex + 1}</b>
      </button>;
    })}
    <img className="assignment-overlay" src={`/media/templates/${session.template.id}`} alt="Khung ảnh" />
    {slots.filter(item => !item.duplicate && item.slot.shotIndex === activeSlot).map(({ slot }) => <div className="assignment-highlight" key={slot.id} style={slotStyle(slot, session.template)} />)}
  </div>;
}

function CustomerAssignment({ session, busy, error, action, draftStore, mirror }: { session: Session; busy: boolean; error: string; action: (path: string, body?: unknown) => Promise<boolean>; draftStore: React.RefObject<AssignmentDraft | null>; mirror: boolean }) {
  const required = templateSelectionCount(session.template);
  const identity = `${session.id}:${session.template.id}:${session.retakes}`;
  const [draft, setDraft] = useState<AssignmentDraft>(() => loadAssignment(session, draftStore.current));
  const { assignments: slotAssignments, active: activeSlot } = draft;
  const submitting = useRef(false);
  const [saving, setSaving] = useState(false);
  function change(assignments: SlotAssignment[], active: number) { const next = { identity, assignments, active }; draftStore.current = next; saveAssignment(next); setDraft(next); }
  function setActiveSlot(active: number) { change(slotAssignments, active); }
  function assignPhoto(photoIndex: number) {
    const next = placePhoto(slotAssignments, activeSlot, photoIndex);
    change(next, nextEmptySlot(next, activeSlot));
  }
  async function composeSelection() {
    if (submitting.current || !isComplete(slotAssignments)) return;
    submitting.current = true; setSaving(true);
    try { if (await action(`/session/${session.id}/select`, { indices: slotAssignments })) await action(`/session/${session.id}/compose`); }
    finally { submitting.current = false; setSaving(false); }
  }
  return <main className="booth assignment-booth" data-flow="ASSIGN" style={{ '--accent': '#66d5ed' } as React.CSSProperties}>
    <header className="booth-header"><span className="wordmark"><Camera size={24} /> VINHUNI <b>PHOTOBOOTH</b></span><span className="edition">SẮP XẾP ẢNH VÀO KHUNG</span></header>
    <section className="assignment-layout">
      <div className="assignment-main">
        <div className="eyebrow">03 / SẮP XẾP ẢNH VÀO KHUNG</div>
        <h1>Chọn ảnh, <em>đặt vào khung.</em></h1>
        <div className="assignment-active">Đang chọn Slot {activeSlot + 1}</div>
        <FrameAssignment session={session} assignments={slotAssignments} activeSlot={activeSlot} onSlotClick={slot => { if (!busy && !saving) setActiveSlot(slot); }} mirror={mirror} />
        <div className="assignment-actions">
          <button className="secondary" disabled={busy || saving} onClick={() => change(createAssignments(required), 0)}>ĐẶT LẠI</button>
          <button className="primary" disabled={busy || saving || !isComplete(slotAssignments)} onClick={() => void composeSelection()}>GHÉP ẢNH <ArrowRight /></button>
        </div>
      </div>
      <aside className="assignment-photos">
          <h2>Ảnh đã chụp <span>{session.shots.length}</span></h2>
          <p>Chạm vào ảnh để đặt vào Slot {activeSlot + 1}.</p>
          <p className="assignment-selected-count">Đã đặt {slotAssignments.filter(photo => photo !== null).length} / {required} vị trí</p>
          <div className="assignment-photo-grid">{session.shots.map((_, photoIndex) => {
            const assignedSlot = slotAssignments.indexOf(photoIndex);
            return <button className={`assignment-photo ${assignedSlot >= 0 ? 'assigned' : ''}`} key={photoIndex} disabled={busy || saving} onClick={() => assignPhoto(photoIndex)}>
              <img src={`/media/sessions/${session.id}/shot/${photoIndex}?take=${session.retakes}`} alt={`Ảnh ${photoIndex + 1}`} />
              <strong>Ảnh {photoIndex + 1}</strong><small>{assignedSlot >= 0 ? `Đang ở Slot ${assignedSlot + 1}` : 'Chạm để chọn'}</small>
            </button>;
          })}</div>

      </aside>
    </section>
    {error && <div className="customer-notice" role="alert">{error}</div>}
  </main>;
}

export function Booth() {
  const { status, frame, connected, count } = useBooth(true);
  const [templates, setTemplates] = useState<Template[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const draftStore = useRef<AssignmentDraft | null>(null);
  const state = status?.state;
  const session = status?.session;
  const audio = useRef<AudioContext | null>(null);
  useEffect(() => { if (state === 'FRAME_SELECTION') void api<Template[]>('/templates').then(t => setTemplates(t.filter(x => x.enabled))).catch(() => setError('Nhân viên đang hỗ trợ. Vui lòng chờ một chút.')); }, [state]);
  useEffect(() => {
    if (!status?.settings.sounds || state !== 'COUNTDOWN' || !audio.current || count < 1) return;
    const oscillator = audio.current.createOscillator(), gain = audio.current.createGain(); oscillator.connect(gain); gain.connect(audio.current.destination); oscillator.frequency.value = count === 1 ? 880 : 520; gain.gain.value = .08; oscillator.start(); oscillator.stop(audio.current.currentTime + .12);
  }, [count, state, status?.settings.sounds]);
  useEffect(() => {
    if (!status?.settings.eventMode) return;
    let lock: WakeLockSentinel | undefined;
    const wake = async () => { try { lock = await navigator.wakeLock?.request('screen'); } catch { /* OS policy can deny wake lock. */ } };
    const visibility = () => { if (document.visibilityState === 'visible') void wake(); };
    const exit = (e: KeyboardEvent) => { if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 'q') { void api('/booth/pause', 'POST'); if (document.fullscreenElement) void document.exitFullscreen(); } };
    const before = (e: BeforeUnloadEvent) => e.preventDefault();
    void wake(); document.addEventListener('visibilitychange', visibility); window.addEventListener('keydown', exit); window.addEventListener('beforeunload', before);
    return () => { void lock?.release(); document.removeEventListener('visibilitychange', visibility); window.removeEventListener('keydown', exit); window.removeEventListener('beforeunload', before); };
  }, [status?.settings.eventMode]);
  async function action(path: string, body?: unknown) { if (busy) return false; setBusy(true); setError(''); try { await api(path, 'POST', body); return true; } catch { setError('Nhân viên đang hỗ trợ. Vui lòng chờ một chút.'); return false; } finally { setBusy(false); } }
  if ((customerPhase(state) === 'ASSIGN' || state === 'FILTER') && session) {
    return <CustomerAssignment key={`${session.id}:${session.template.id}:${session.retakes}`} session={session} busy={busy} error={error} action={action} draftStore={draftStore} mirror={status.settings.mirrorFinal} />;
  }
  function begin() { audio.current ??= new AudioContext(); void audio.current.resume(); void action('/booth/select'); }
  const final = session ? `/media/sessions/${session.id}/final?take=${session.retakes}` : '';
  const live = ['LIVE_VIEW', 'READY', 'COUNTDOWN', 'CAPTURING', 'BETWEEN_SHOTS'].includes(state ?? '');
  return <main data-flow={customerPhase(state)} className={`booth ${status?.settings.eventMode ? 'event-mode' : ''}`} style={{ '--accent': status?.settings.accent } as React.CSSProperties}>
    <header className="booth-header"><span className="wordmark"><Camera size={24} /> VINHUNI <b>PHOTOBOOTH</b></span><span className="edition">MAKE A LITTLE MEMORY</span><button className="icon-button" aria-label="Toàn màn hình" onClick={() => void document.documentElement.requestFullscreen().catch(() => setError('Bấm F11 để mở toàn màn hình.'))}><Expand size={20} /></button></header>
    {!connected || !status ? <section className="booth-center"><div className="orb"><Camera size={54} /></div><h1>Hẹn bạn trong giây lát</h1><p>Gian chụp ảnh đang được chuẩn bị.</p></section> : <>
      {state === 'IDLE' && <section className="welcome"><div className="eyebrow"><span /> {status.settings.eventTitle}</div><h1>Một khoảnh khắc.<br /><em>Thật nhiều kỷ niệm.</em></h1><p>Cùng bạn bè tạo nên tấm ảnh của riêng mình.</p><button className="primary jumbo" onClick={begin} disabled={busy}>BẮT ĐẦU <ArrowRight /></button><div className="welcome-details"><span><Sparkles size={17} /> Chọn khung yêu thích</span><span><Camera size={17} /> Tự tin tạo dáng</span><span><Heart size={17} /> Mang kỷ niệm về</span></div><div className="decor-card card-one"><div /><div /><span>GOOD TIMES, GREAT FRIENDS.</span></div><div className="decor-card card-two"><div /><span>VINHUNI / 2026</span></div></section>}
      {state === 'FRAME_SELECTION' && <section className="frame-selection"><div className="eyebrow">01 / CHỌN KHUNG ẢNH</div><h1>Khung nào là <em>gu của bạn?</em></h1><p>Mỗi khung ảnh, một cách lưu giữ khoảnh khắc.</p><div className="frame-grid">{templates.map(t => <button className="frame-card" key={t.id} disabled={busy} onClick={() => void action('/session', { templateId: t.id })}><div className="frame-art"><img src={`/media/templates/${t.id}`} alt={t.name} /></div><div className="frame-description"><span><strong>{t.name}</strong><small>{t.shotsRequired} ảnh · {t.orientation === 'portrait' ? 'Khung dọc' : 'Khung ngang'}</small></span><ArrowRight /></div></button>)}</div></section>}
      {live && session && <section className="capture-layout"><div className="capture-title"><div><div className="eyebrow">02 / TỎA SÁNG THEO CÁCH CỦA BẠN</div><h2>{state === 'BETWEEN_SHOTS' ? 'Chuẩn bị kiểu tiếp theo!' : state === 'COUNTDOWN' ? 'Nhìn vào máy ảnh nhé!' : 'Sẵn sàng cười thật tươi?'}</h2></div><span className="pill">{Math.min(session.shots.length + 1, templateCaptureCount(session.template))} / {templateCaptureCount(session.template)} ảnh</span></div><div className="viewfinder">{frame && <img src={frame} alt="Live view" style={{ transform: status.settings.mirrorPreview ? 'scaleX(-1)' : undefined, objectFit: status.settings.liveViewFit }} />}<div className="viewfinder-corner tl" /><div className="viewfinder-corner br" /><span className="live-tag"><i /> {status.camera.provider === 'mock' ? 'MOCK · ẢNH MẪU' : 'LIVE'}</span>{state === 'COUNTDOWN' && <div key={count} className="countdown">{count || status.settings.countdown}</div>}{state === 'CAPTURING' && <><div className={status.settings.flash ? 'flash' : ''} /><div className="capture-message">CƯỜI LÊN NÀO!</div></>}{state === 'BETWEEN_SHOTS' && <div className="capture-message"><Check /> ẢNH {session.shots.length}/{templateCaptureCount(session.template)}</div>}</div><div className="capture-footer"><span>{session.template.name}</span><div className="shot-dots">{Array.from({ length: templateCaptureCount(session.template) }, (_, i) => <span className={i < session.shots.length ? 'done' : ''} key={i}>{i < session.shots.length ? <Check size={16} /> : i + 1}</span>)}</div>{state === 'LIVE_VIEW' ? <button className="primary" disabled={busy} onClick={() => void action(`/session/${session.id}/capture`)}><Camera /> CHỤP ẢNH</button> : <span className="muted">Hãy giữ dáng một chút nhé</span>}</div></section>}
      {(state === 'COMPOSING' || state === 'PRINTING') && <section className="booth-center"><div className="spinner" /><h1>{state === 'COMPOSING' ? 'Kỷ niệm đang thành hình…' : 'Đang in ảnh…'}</h1><p>Chờ một chút nhé, ảnh đẹp sắp đến rồi.</p></section>}
      {state === 'FINAL_PREVIEW' && session && <section className="result-layout"><div className="final-photo"><img src={final} alt="Ảnh của bạn" /></div><div><div className="eyebrow">03 / KHOẢNH KHẮC CỦA BẠN</div><h1>Đẹp như<br /><em>chính bạn.</em></h1><p>Ảnh của bạn đã sẵn sàng.<br />Bạn có muốn giữ khoảnh khắc này?</p><button className="primary jumbo" disabled={busy} onClick={() => void action(`/session/${session.id}/confirm`)}>XÁC NHẬN <Check /></button>{status.settings.allowRetake && session.retakes < status.settings.maxRetake && <button className="secondary" disabled={busy} onClick={() => void action(`/session/${session.id}/retake`)}><RotateCcw size={18} /> CHỤP LẠI ({status.settings.maxRetake - session.retakes})</button>}</div></section>}
      {state === 'QR_READY' && session && <section className="result-layout"><div className="final-photo"><img src={final} alt="Ảnh hoàn chỉnh" /></div><div><div className="eyebrow">04 / MANG KỶ NIỆM VỀ</div><h1>Ảnh đẹp.<br /><em>Gửi bạn nhé!</em></h1><p>Ảnh đã được lưu.</p>{session.printStatus === 'sent' && <p><Check size={18} /> Ảnh đã được gửi tới máy in.</p>}{!status.settings.autoPrint && session.printStatus === 'none' && <button className="secondary" disabled={busy} onClick={() => void action(`/print/${session.id}`)}>IN ẢNH</button>}<button className="primary" disabled={busy} onClick={() => void action('/booth/finish')}>HOÀN TẤT <ArrowRight /></button></div></section>}
      {state === 'THANK_YOU' && <section className="booth-center booth-thank-you"><div className="orb"><Heart size={54} /></div><div className="eyebrow">A MOMENT TO REMEMBER</div><h1>Cảm ơn bạn<br /><em>đã ghé chơi!</em></h1><p>Hẹn gặp lại ở những khoảnh khắc tiếp theo.</p>{session && <CloudQr session={session} />}<button className="primary" disabled={busy} onClick={() => void action('/booth/start')}>PHIÊN MỚI <ArrowRight /></button></section>}
      {(state === 'ERROR' || state === 'MAINTENANCE') && <section className="booth-center"><div className="orb"><Camera size={54} /></div><h1>Chờ chúng mình một chút</h1><p>Nhân viên đang hỗ trợ để bạn có tấm ảnh đẹp nhất.</p>{session?.confirmed && <p>Ảnh của bạn đã được lưu an toàn.</p>}</section>}
    </>}
    {error && <div className="customer-notice" role="alert">{error}</div>}<footer className="booth-footer"><span>VINHUNI · MADE OF MEMORIES</span><span>YOUR FRIENDS. YOUR MOMENT.</span></footer>
  </main>;
}
