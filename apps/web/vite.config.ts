import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { VitePWA } from 'vite-plugin-pwa';
export default defineConfig({ plugins: [react(), tailwindcss(), VitePWA({ registerType: 'prompt', manifest: { name: 'VINHUNI PHOTOBOOTH', short_name: 'Photobooth', theme_color: '#0a1729', background_color: '#0a1729', display: 'standalone', icons: [{ src: '/branding/icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' }] }, workbox: { navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//, /^\/media\//, /^\/p\//], maximumFileSizeToCacheInBytes: 4000000 } })], server: { proxy: { '/api': 'http://127.0.0.1:4310', '/media': 'http://127.0.0.1:4310', '/hubs': { target: 'http://127.0.0.1:4310', ws: true } } } });
