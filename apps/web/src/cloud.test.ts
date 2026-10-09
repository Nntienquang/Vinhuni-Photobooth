import { afterEach, describe, expect, it, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import type { VercelRequest, VercelResponse } from '@vercel/node';
const mock = vi.hoisted(() => ({ put: vi.fn().mockResolvedValue({}) }));
vi.mock('@vercel/blob', () => ({ put: mock.put }));
import handler from '../api/upload';
function response() {
  const result = { code: 200, body: {} as unknown, setHeader: vi.fn(), status(code: number) { this.code = code; return this; }, json(body: unknown) { this.body = body; return this; } };
  return result;
}
function configure() { vi.stubEnv('PHOTOBOOTH_UPLOAD_TOKEN', 'test-secret'); vi.stubEnv('BLOB_READ_WRITE_TOKEN', 'test-blob'); vi.stubEnv('PHOTOBOOTH_PUBLIC_URL', 'https://photos.example.test'); }
afterEach(() => { vi.unstubAllEnvs(); vi.clearAllMocks(); });
describe('cloud upload boundary', () => {
  it('fails closed without secrets', async () => { vi.stubEnv('PHOTOBOOTH_UPLOAD_TOKEN', ''); const res = response(); await handler({ method: 'POST', headers: {} } as VercelRequest, res as unknown as VercelResponse); expect(res.code).toBe(503); });
  it('rejects incorrect bearer token before parsing', async () => { configure(); const res = response(); await handler({ method: 'POST', headers: { authorization: 'Bearer invalid' } } as VercelRequest, res as unknown as VercelResponse); expect(res.code).toBe(401); expect(mock.put).not.toHaveBeenCalled(); });
  it.each([['valid', '11111111-1111-4111-8111-111111111111', Buffer.from([255, 216, 255, 217]), 200], ['bad UUID', '../secret', Buffer.from([255, 216, 255, 217]), 400], ['bad image', '11111111-1111-4111-8111-111111111111', Buffer.from('not an image'), 400]] as const)('%s upload', async (_name, shareId, file, expected) => {
    configure(); const stream = new PassThrough(); const req = Object.assign(stream, { method: 'POST', headers: { authorization: 'Bearer test-secret', 'content-type': 'multipart/form-data; boundary=booth-test' } }) as unknown as VercelRequest;
    const res = response(); const work = handler(req, res as unknown as VercelResponse);
    stream.end(Buffer.concat([Buffer.from(`--booth-test\r\nContent-Disposition: form-data; name="shareId"\r\n\r\n${shareId}\r\n--booth-test\r\nContent-Disposition: form-data; name="file"; filename="test.jpg"\r\nContent-Type: image/jpeg\r\n\r\n`), file, Buffer.from('\r\n--booth-test--\r\n')]));
    await work; expect(res.code).toBe(expected);
    if (expected === 200) { expect(mock.put).toHaveBeenCalledWith(`photobooth/${shareId}.jpg`, file, expect.objectContaining({ access: 'private', addRandomSuffix: false })); expect(res.body).toEqual({ shareId, shareUrl: `https://photos.example.test/p/${shareId}` }); } else expect(mock.put).not.toHaveBeenCalled();
  });
});
