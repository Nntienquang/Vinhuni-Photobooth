import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { QRCodeSVG } from 'qrcode.react';
import type { Session } from '@booth/shared';
import { CloudQr } from './pages/Booth';

describe('local share QR', () => {
  const session = { uploadStatus: 'uploaded', shareUrl: 'https://share.example/p/session', imageUrl: 'https://store.public.blob.vercel-storage.com/final.jpg' } as Session;
  it('encodes the share page using the existing local SVG library and a quiet zone', () => {
    const html = renderToStaticMarkup(<CloudQr session={session} />);
    const expected = renderToStaticMarkup(<QRCodeSVG value={session.shareUrl!} size={300} level="M" marginSize={4} bgColor="#ffffff" fgColor="#000000" title="Quét mã để tải ảnh" style={{ width: 'min(300px, 80vw)', height: 'auto', display: 'block', margin: '0 auto' }} />);
    expect(html).toContain(expected);
    expect(html).not.toContain('<img');
    expect(html).not.toContain(session.imageUrl);
    expect(html).toContain('Ảnh chất lượng gốc');
  });
  it('shows upload progress and safe failure without a QR', () => {
    const progress = renderToStaticMarkup(<CloudQr session={{ ...session, uploadStatus: 'uploading' }} />);
    expect(progress).toContain('Đang tạo mã QR...');
    expect(progress).not.toContain('<svg');
    const failed = renderToStaticMarkup(<CloudQr session={{ ...session, uploadStatus: 'failed' }} />);
    expect(failed).toContain('Chưa thể tạo mã QR.');
    expect(failed).toContain('Ảnh vẫn đã được lưu và in bình thường.');
    expect(failed).not.toContain('<svg');
  });
  it('omits cloud UI when disabled', () => {
    expect(renderToStaticMarkup(<CloudQr session={{ ...session, uploadStatus: 'none' }} />)).toBe('');
  });
});
