import { cp, mkdir } from 'node:fs/promises';
await mkdir('apps/bridge/wwwroot', { recursive: true });
await cp('apps/web/dist', 'apps/bridge/wwwroot', { recursive: true });
