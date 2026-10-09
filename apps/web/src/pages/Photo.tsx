import { useParams } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { Camera, Download } from 'lucide-react';
export function Photo() {
  const { shareId } = useParams(); const [error, setError] = useState(false); const [copied, setCopied] = useState(false);
  const valid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(shareId ?? '');
  const [signed, setSigned] = useState('');
  useEffect(() => { if (!valid) return; let active = true; void fetch(`/api/photo/${shareId}?signed=1`).then(async r => { if (!r.ok) throw new Error('Unavailable'); return await r.json() as { url: string }; }).then(data => { if (active) setSigned(data.url); }).catch(() => { if (active) setError(true); }); return () => { active = false; }; }, [shareId, valid]);
  return <main className="share-page"><Camera size={36} /><div className="eyebrow">VINHUNI PHOTOBOOTH</div><h1>Một kỷ niệm,<br /><em>dành riêng cho bạn.</em></h1>{!valid || error ? <p>Ảnh chưa khả dụng hoặc đã hết thời gian lưu trữ.</p> : <><img className="shared-photo" src={signed || undefined} onError={() => setError(true)} alt="Ảnh photobooth của bạn" /><a className="primary" href={`/api/photo/${shareId}?download=1`}><Download size={20} /> TẢI ẢNH</a><button className="secondary" onClick={() => void navigator.clipboard.writeText(location.href).then(() => setCopied(true))}>{copied ? 'Đã sao chép' : 'Sao chép liên kết'}</button></>}<p>Keep the moment. Share the joy.</p></main>;
}
