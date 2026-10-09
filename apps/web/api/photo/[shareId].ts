import type { VercelRequest, VercelResponse } from '@vercel/node';
import { get, issueSignedToken, presignUrl } from '@vercel/blob';
import { Readable } from 'node:stream';
export default async function handler(req: VercelRequest, res: VercelResponse) {
  res.setHeader('Cache-Control', 'private, no-store'); res.setHeader('X-Content-Type-Options', 'nosniff'); res.setHeader('Referrer-Policy', 'no-referrer');
  if (req.method !== 'GET') return res.status(405).end();
  const id = req.query.shareId;
  if (typeof id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) return res.status(400).end();
  try {
    for (const extension of ['jpg', 'png']) {
      const result = await get(`photobooth/${id}.${extension}`, { access: 'private' });
      if (!result || result.statusCode !== 200 || !result.stream) continue;
      if (req.query.signed === '1') {
        await result.stream.cancel();
        const pathname = `photobooth/${id}.${extension}`, validUntil = Date.now() + 5 * 60 * 1000;
        const token = await issueSignedToken({ pathname, operations: ['get'], validUntil });
        const { presignedUrl } = await presignUrl(token, { pathname, operation: 'get', access: 'private', validUntil });
        return res.status(200).json({ url: presignedUrl, expiresAt: validUntil });
      }
      res.setHeader('Content-Type', extension === 'png' ? 'image/png' : 'image/jpeg');
      if (req.query.download === '1') res.setHeader('Content-Disposition', `attachment; filename="vinhuni-${id}.${extension}"`);
      return Readable.fromWeb(result.stream as import('node:stream/web').ReadableStream).pipe(res);
    }
    return res.status(404).json({ error: 'Photo not found' });
  } catch { return res.status(503).json({ error: 'Photo temporarily unavailable' }); }
}
