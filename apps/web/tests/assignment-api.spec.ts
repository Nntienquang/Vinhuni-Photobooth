import { test, expect } from '@playwright/test';

test('POST assign persists logical slots; completion, print and history remain usable', async ({ page, context, request }) => {
  const webcam = process.env.BOOTH_VERIFY_WEBCAM === '1';
  const headers = { 'X-Booth-Client': '1' };
  const post = (path: string, data?: unknown) => request.post('/api' + path, { headers, data });
  const current = async () => (await request.get('/api/session/current')).json();
  const status = await (await request.get('/api/system/status')).json();
  expect(status.printProvider).toBe('mock');
  await post('/booth/start');
  await post('/camera/provider', { provider: webcam ? 'webcam' : 'mock', autoConnect: true, startLiveView: true });
  await post('/booth/start');
  const settings = await (await request.get('/api/settings')).json();
  await request.put('/api/settings', { headers, data: { ...settings, countdown: 3, betweenShots: 0, autoPrint: false, cloudEnabled: false, thankYouTimeout: 3 } });
  if (webcam) await context.grantPermissions(['camera']);
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/booth');
  await page.getByRole('button', { name: 'BẮT ĐẦU' }).click();
  await page.getByRole('button', { name: webcam ? /Khung moi/ : /Classic 4 Shot/ }).click();
  await expect.poll(() => page.getByAltText('Live view').evaluate((img: HTMLImageElement) => img.naturalWidth), { timeout: 15000 }).toBeGreaterThan(0);
  await page.getByRole('button', { name: 'CHỤP ẢNH', exact: true }).click();
  await expect(page.locator('.assignment-photo')).toHaveCount(6, { timeout: 40000 });
  const session = await current();
  expect(session.state).toBe('ASSIGN');
  expect(session.shots).toHaveLength(6);
  const order = [0,1,3,5];
  for (const i of order) await page.locator('.assignment-photo').nth(i).click();
  const left = page.locator('.assignment-slot:not(.duplicate-slot) img');
  const right = page.locator('.assignment-slot.duplicate-slot img');
  for (let i = 0; i < order.length; i++) {
    await expect(left.nth(i)).toHaveAttribute('src', new RegExp(`/shot/${order[i]}\\?`));
    await expect(right.nth(i)).toHaveAttribute('src', new RegExp(`/shot/${order[i]}\\?`));
  }
  expect((await post('/session/not-current/assign', { indices: order })).status()).toBe(409);
  for (const indices of [null, [], [0,1,3], [0,1,3,-1], [0,1,3,6], [0,1,3,3]]) {
    expect((await post(`/session/${session.id}/assign`, { indices })).status()).toBe(400);
    expect((await current()).state).toBe('ASSIGN');
    expect((await current()).selectedShots).toEqual([]);
  }
  const assigned = await post(`/session/${session.id}/assign`, { indices: order });
  expect(assigned.status()).toBe(200);
  const accepted = await current();
  expect(accepted.state).toBe('FILTER');
  expect(accepted.selectedShots).toEqual(order);
  expect(accepted.shots).toEqual(session.shots);
  const history = await (await request.get('/api/sessions')).json();
  expect(history.find((s: { id: string }) => s.id === session.id).selectedShots).toEqual(order);
  // Idempotent retry must not reset captures or slot order.
  expect((await post(`/session/${session.id}/assign`, { indices: order })).status()).toBe(200);
  // This checkout has no filter source/endpoint. Verify downstream composition
  // independently via its existing API, without pretending a filter was applied.
  expect((await post(`/session/${session.id}/compose`)).status()).toBe(200);
  await expect(page.getByRole('button', { name: 'XÁC NHẬN' })).toBeVisible();
  expect((await current()).selectedShots).toEqual(order);
  expect((await request.get(`/media/sessions/${session.id}/final`)).status()).toBe(200);
  await page.getByRole('button', { name: 'XÁC NHẬN' }).click();
  await page.getByRole('button', { name: 'IN ẢNH', exact: true }).click();
  await expect.poll(async () => (await current()).printStatus).toBe('sent');
  expect((await post(`/session/${session.id}/assign`, { indices: order })).status()).toBe(409);
  await page.getByRole('button', { name: 'HOÀN TẤT' }).click();
  await expect(page.getByRole('button', { name: 'BẮT ĐẦU' })).toBeVisible({ timeout: 10000 });
  const done = (await (await request.get('/api/sessions')).json()).find((s: { id: string }) => s.id === session.id);
  expect(done.state).toBe('THANK_YOU'); expect(done.printStatus).toBe('sent'); expect(done.confirmed).toBe(true); expect(done.error).toBeNull();
  await page.goto('/admin/history');
  const row = page.locator('tr').filter({ hasText: session.id.slice(0,8) });
  await expect(row).toContainText('THANK_YOU'); await expect(row).toContainText('sent');
  await expect.poll(() => row.getByAltText('Final').evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1200);
  expect(errors).toEqual([]);
  console.log('Assignment API verified:', { webcam, id: session.id, state: done.state, print: done.printStatus, order: done.selectedShots });
  await post('/camera/provider', { provider: 'mock', autoConnect: true, startLiveView: true });
  await post('/booth/start');
  await request.put('/api/settings', { headers, data: settings });
});
