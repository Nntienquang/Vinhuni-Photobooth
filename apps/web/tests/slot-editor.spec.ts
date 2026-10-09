import { test, expect } from '@playwright/test';
import { createHash } from 'node:crypto';

test('uploaded PNG stays unchanged while source geometry, duplicates and crop controls are edited', async ({ page, request }) => {
  const status = await (await request.get('/api/system/status')).json();
  expect(status.camera.provider).toBe('mock'); expect(status.printProvider).toBe('mock');
  await page.goto('/admin/templates');
  await page.getByRole('button', { name: 'THEM KHUNG ANH' }).click();
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 1200; canvas.height = 1800;
    const context = canvas.getContext('2d')!; context.fillStyle = '#14384a'; context.fillRect(0, 0, 1200, 100);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  const bytes = Buffer.from(png, 'base64');
  await page.locator('input[accept=".png,image/png"]').setInputFiles({ name: 'unchanged-frame.png', mimeType: 'image/png', buffer: bytes });
  const name = `Editor validation ${Date.now()}`;
  await page.getByLabel('Ten khung').fill(name);
  await page.getByLabel('Snap to edges').uncheck(); await page.getByLabel('Snap to center').uncheck();
  const source = page.locator('.slot-editor-hit:not(.derived)').first();
  const before = await source.boundingBox(); expect(before).not.toBeNull();
  await page.mouse.move(before!.x + before!.width / 2, before!.y + before!.height / 2);
  await page.mouse.down(); await page.mouse.move(before!.x + before!.width / 2 + 13, before!.y + before!.height / 2 + 13); await page.mouse.up();
  await expect(page.getByLabel('X', { exact: true })).toHaveValue('130');
  const handle = page.locator('.slot-editor-hit.selected:not(.derived) .handle-se'); const box = await handle.boundingBox();
  await page.mouse.move(box!.x + 6, box!.y + 6); await page.mouse.down(); await page.mouse.move(box!.x + 19, box!.y + 19); await page.mouse.up();
  await expect(page.getByLabel('Width', { exact: true }).last()).toHaveValue('490');
  await page.getByLabel('Visual slot canvas').focus(); await page.keyboard.press('ArrowRight'); await page.keyboard.press('Shift+ArrowDown');
  await expect(page.getByLabel('X', { exact: true })).toHaveValue('131');
  await expect(page.getByLabel('Y', { exact: true })).toHaveValue('230');
  await page.getByLabel('X', { exact: true }).fill('75');
  await page.getByLabel('Width', { exact: true }).last().fill('400');
  await page.getByLabel('Radius', { exact: true }).fill('20');
  await page.getByLabel('Focal X').fill('0.8');
  await page.getByLabel('Duplicate mode').selectOption('mirror');
  await page.getByLabel('Duplicate offset X').fill('610');
  await page.getByLabel('Duplicate offset Y').fill('10');
  await expect(page.getByLabel('X', { exact: true })).toHaveValue('75');
  const derived = page.locator('.slot-editor-hit.derived').first();
  await expect(derived).toHaveCSS('left', '238.875px'); // (610 + 600 - 75 - 400) * 390/1200
  await page.getByLabel('Frame opacity').fill('0.5');
  await expect(page.locator('.slot-editor-overlay')).toHaveCSS('opacity', '0.5');
  await page.getByRole('button', { name: 'PREVIEW TEMPLATE' }).click();
  await expect(page.locator('.slot-editor-hit')).toHaveCount(0);
  await expect(page.locator('.slot-editor-overlay')).toHaveCSS('opacity', '1');
  await page.getByRole('button', { name: 'EDIT SLOTS' }).click();
  await page.getByRole('button', { name: 'ADD PHOTO SLOT' }).click();
  await expect(page.locator('.slot-editor-hit:not(.derived)')).toHaveCount(5);
  await page.getByRole('button', { name: 'DELETE', exact: true }).click();
  await page.getByRole('button', { name: 'SAVE TEMPLATE' }).click();
  await expect(page.getByRole('heading', { name: 'Visual slot editor' })).toHaveCount(0);
  const templates = await (await request.get('/api/templates')).json();
  const saved = templates.find((t: { name: string }) => t.name === name); expect(saved).toBeTruthy();
  try {
    expect(saved.slots[0]).toMatchObject({ x: 75, y: 230, width: 400, borderRadius: 20, focalX: .8 });
    expect(saved.duplicateMode).toBe('mirror'); expect(saved.duplicateOffsetX).toBe(610);
    const stored = await (await request.get(`/media/templates/${saved.id}`)).body();
    expect(createHash('sha256').update(stored).digest('hex')).toBe(createHash('sha256').update(bytes).digest('hex'));
  } finally { await request.delete(`/api/templates/${saved.id}`, { headers: { 'X-Booth-Client': '1' } }); }
});
