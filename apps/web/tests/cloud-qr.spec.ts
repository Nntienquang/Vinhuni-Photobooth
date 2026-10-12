import { test, expect } from '@playwright/test';

// Run against a static preview, never a live camera/printing bridge.
test('THANK_YOU cloud states and new-session action remain usable', async ({ page }) => {
  const base = process.env.BOOTH_QR_PREVIEW_URL;
  test.skip(!base, 'Set BOOTH_QR_PREVIEW_URL to an isolated Vite preview');
  const status = {
    state: 'THANK_YOU', session: { id: 'qr-preview', template: { id: 'test', slots: [] }, shots: [], selectedShots: [], retakes: 0,
      state: 'THANK_YOU', confirmed: true, printStatus: 'sent', uploadStatus: 'uploading', shareUrl: null as string | null },
    settings: { sounds: false, eventMode: false, autoPrint: false, accent: '#66d5ed' }, camera: { provider: 'mock', connected: true, liveView: false },
  };
  let publish = () => {};
  let resets = 0;
  await page.route('**/api/**', route => {
    if (route.request().url().endsWith('/booth/start')) resets++;
    return route.fulfill({ json: status });
  });
  await page.route('**/hubs/booth/negotiate*', route => route.fulfill({ json: {
    negotiateVersion: 1, connectionId: 'preview', connectionToken: 'preview',
    availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }],
  } }));
  await page.routeWebSocket('**/hubs/booth*', socket => {
    publish = () => socket.send(JSON.stringify({ type: 1, target: 'sessionChanged', arguments: [status] }) + '\x1e');
    socket.onMessage(message => {
      for (const part of String(message).split('\x1e').filter(Boolean)) {
        const incoming = JSON.parse(part);
        if (incoming.protocol) socket.send('{}\x1e');
        if (incoming.invocationId) socket.send(JSON.stringify({ type: 3, invocationId: incoming.invocationId }) + '\x1e');
      }
    });
  });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(base + '/booth');
  await expect(page.locator('.cloud-qr')).toContainText('Đang tạo mã QR...');
  await page.getByRole('button', { name: 'PHIÊN MỚI' }).click();
  expect(resets).toBe(1);
  status.session.uploadStatus = 'failed';
  publish();
  await expect(page.locator('.cloud-qr')).toContainText('Chưa thể tạo mã QR.');
  await page.getByRole('button', { name: 'PHIÊN MỚI' }).click();
  expect(resets).toBe(2);
  status.session.uploadStatus = 'uploaded';
  status.session.shareUrl = 'https://vinhuni-photobooth-share.vercel.app/p/preview';
  publish();
  const qr = page.locator('.cloud-qr svg');
  await expect(qr).toBeVisible();
  await expect(page.locator('.cloud-qr')).toContainText('Ảnh chất lượng gốc');
  for (const viewport of [{ width: 1920, height: 1080 }, { width: 1280, height: 800 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport);
    await page.evaluate(() => window.scrollTo(0, 0));
    const bounds = await qr.boundingBox();
    expect(bounds!.width).toBe(300);
    expect(bounds!.y).toBeGreaterThanOrEqual(0);
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height);
    await page.getByRole('button', { name: 'PHIÊN MỚI' }).scrollIntoViewIfNeeded();
    await expect(page.getByRole('button', { name: 'PHIÊN MỚI' })).toBeInViewport();
    await page.screenshot({ path: `test-results/cloud-qr-${viewport.width}.png`, fullPage: true });
  }
  expect(errors).toEqual([]);
});
