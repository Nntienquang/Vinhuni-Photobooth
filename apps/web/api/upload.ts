import type { VercelRequest, VercelResponse } from '@vercel/node';
import { put } from '@vercel/blob';
import busboy from 'busboy';
import { timingSafeEqual } from 'node:crypto';
export const config = { api: { bodyParser: false } };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export default async function handler(req: VercelRequest, res: VercelResponse) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.method !== 'POST') return res.status(405).json({ error: 'POST required' });
  const token = process.env.PHOTOBOOTH_UPLOAD_TOKEN;
  if (!token || !process.env.BLOB_READ_WRITE_TOKEN || !process.env.PHOTOBOOTH_PUBLIC_URL) return res.status(503).json({ error: 'Cloud not configured' });
  const supplied = Buffer.from(req.headers.authorization ?? ''), expected = Buffer.from(`Bearer ${token}`);
  if (supplied.length !== expected.length || !timingSafeEqual(supplied, expected)) return res.status(401).json({ error: 'Unauthorized' });
  try {
    const { file, shareId } = await new Promise<{ file: Buffer; shareId: string }>((resolve, reject) => {
      const parser = busboy({ headers: req.headers, limits: { fileSize: 4_000_000, files: 1, fields: 1, fieldSize: 100, parts: 3 } });
      let id = '', bytes: Buffer | undefined; let failed = false;
      const fail = () => { failed = true; reject(new Error('Invalid upload')); };
      parser.on('field', (name, value, info) => { if (name !== 'shareId' || info.valueTruncated) fail(); else id = value; });
      parser.on('file', (name, stream, info) => { const chunks: Buffer[] = []; if (name !== 'file' || !['image/jpeg', 'image/png'].includes(info.mimeType)) { stream.resume(); fail(); return; } stream.on('limit', fail); stream.on('error', fail); stream.on('data', (b: Buffer) => chunks.push(b)); stream.on('end', () => { bytes = Buffer.concat(chunks); }); });
      parser.on('filesLimit', fail); parser.on('fieldsLimit', fail); parser.on('partsLimit', fail); parser.on('error', fail); req.on('aborted', fail);
      parser.on('finish', () => { if (!failed && bytes?.length && uuid.test(id)) resolve({ file: bytes, shareId: id }); else fail(); }); req.pipe(parser);
    });
    const png = file.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
    const jpg = file.length > 3 && file[0] === 255 && file[1] === 216 && file[2] === 255;
    if (!png && !jpg) return res.status(400).json({ error: 'JPEG or PNG required' });
    // Stable UUID path makes retries idempotent. Private objects are streamed by the photo API.
    await put(`photobooth/${shareId}.${png ? 'png' : 'jpg'}`, file, { access: 'private', addRandomSuffix: false, allowOverwrite: true, contentType: png ? 'image/png' : 'image/jpeg' });
    const base = new URL(process.env.PHOTOBOOTH_PUBLIC_URL); if (base.protocol !== 'https:') throw new Error('HTTPS required');
    return res.status(200).json({ shareId, shareUrl: new URL(`/p/${shareId}`, base).href });
  } catch { return res.status(400).json({ error: 'Upload failed; verify image and limit of 4 MB' }); }
}
