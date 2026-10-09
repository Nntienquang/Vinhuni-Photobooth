import { test, expect } from '@playwright/test';

test('restored direct frame assignment and composition', async ({ page, context, request }) => {
  const webcam = process.env.BOOTH_VERIFY_WEBCAM === '1';
  const headers = { 'X-Booth-Client': '1' };
  const post = (path: string, data?: unknown) => request.post('/api' + path, { headers, data });
  const errors: string[] = [], badRequests: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => { if (/\/shot\/-1|\/(assign|assignment|filter|reorder)$/.test(req.url())) badRequests.push(req.url()); });
  const status = await (await request.get('/api/system/status')).json();
  expect(status.printProvider).toBe('mock');
  await post('/booth/start');
  expect((await post('/camera/provider', { provider: webcam ? 'webcam' : 'mock', autoConnect: true, startLiveView: true })).ok()).toBe(true);
  await post('/booth/start');
  const settings = await (await request.get('/api/settings')).json();
  await request.put('/api/settings', { headers, data: { ...settings, countdown: 3, betweenShots: 0, autoPrint: false, cloudEnabled: false } });
  if (webcam) await context.grantPermissions(['camera']);
  await page.goto('/booth');
  await page.getByRole('button', { name: 'BẮT ĐẦU' }).click();
  await page.getByRole('button', { name: webcam ? /Khung moi/ : /Classic 4 Shot/ }).click();
  if (webcam) {
    // Uses the physical getUserMedia device; no fake-device flag or injected frames.
    const permission = await page.evaluate(async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        const settings = stream.getVideoTracks()[0].getSettings();
        stream.getTracks().forEach(track => track.stop());
        return { width: settings.width, height: settings.height, error: '' };
      } catch (error) { return { error: String(error) }; }
    });
    expect(permission.error, 'Physical webcam must be available').toBe('');
    console.log('Physical webcam:', permission);
  }
  await expect.poll(() => page.getByAltText('Live view').evaluate((img: HTMLImageElement) => img.naturalWidth), { timeout: 15000 }).toBeGreaterThan(0);
  await page.getByRole('button', { name: 'CHỤP ẢNH', exact: true }).click();
  for (let count = 1; count <= 6; count++) await expect(page.locator('.capture-title .pill')).toHaveText(`${count} / 6 ảnh`, { timeout: 10000 });
  await expect(page.locator('main')).toHaveAttribute('data-flow', 'ASSIGN', { timeout: 10000 });
  const session = await (await request.get('/api/session/current')).json();
  expect(session.state).toBe('PHOTO_SELECTION');
  expect(session.shots).toHaveLength(6);
  expect(session.template.selectionCount).toBe(4);
  expect(session.template.duplicateStrip).toBe(true);
  await expect(page.locator('.assignment-photo')).toHaveCount(6);
  await expect.poll(() => page.locator('.assignment-overlay').evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1200);
  const compose = page.getByRole('button', { name: /GHÉP ẢNH/ });
  await expect(compose).toBeDisabled();
  for (const index of [2,4]) await page.locator('.assignment-photo').nth(index).click();
  await page.reload();
  await expect(page.locator('.assignment-active')).toContainText('Slot 3');
  for (const index of [0,5]) await page.locator('.assignment-photo').nth(index).click();
  await expect(compose).toBeEnabled();
  await expect(page.locator('main')).toHaveAttribute('data-flow', 'ASSIGN');
  await expect(page.locator('.filter-picker')).toHaveCount(0);
  const left = page.locator('.assignment-slot:not(.duplicate-slot) img');
  const right = page.locator('.assignment-slot.duplicate-slot img');
  const order = [2,4,0,5];
  for (let i = 0; i < order.length; i++) {
    await expect(left.nth(i)).toHaveAttribute('src', new RegExp(`/shot/${order[i]}\\?`));
    await expect(right.nth(i)).toHaveAttribute('src', new RegExp(`/shot/${order[i]}\\?`));
  }
  await page.screenshot({ path: `../../.tools/${webcam ? 'webcam' : 'mock'}-restored-assignment.png`, fullPage: true });
  await compose.click();
  await expect(page.getByRole('button', { name: 'XÁC NHẬN' })).toBeVisible({ timeout: 20000 });
  const final = await (await request.get('/api/session/current')).json();
  expect(final.state).toBe('FINAL_PREVIEW');
  expect(final.error).toBeNull();
  expect(final.selectedShots).toEqual(order);
  await expect.poll(() => page.getByAltText('Ảnh của bạn').evaluate((img: HTMLImageElement) => [img.naturalWidth,img.naturalHeight])).toEqual([1200,1800]);
  await page.screenshot({ path: `../../.tools/${webcam ? 'webcam' : 'mock'}-restored-preview.png`, fullPage: true });
  expect(errors).toEqual([]);
  expect(badRequests).toEqual([]);
  console.log('Verified session:', final.id, final.state, final.shots.length, final.selectedShots);
  await post('/booth/start');
  await post('/camera/provider', { provider: 'mock', autoConnect: true, startLiveView: true });
  await post('/booth/start');
  await request.put('/api/settings', { headers, data: settings });
});
