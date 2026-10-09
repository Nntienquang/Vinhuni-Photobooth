import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useEffect, useState } from 'react';
import type { Status } from '@booth/shared';

export async function api<T = void>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const form = body instanceof FormData;
  const response = await fetch('/api' + path, { method, headers: { 'X-Booth-Client': '1', ...(!form && body !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: form ? body : body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) { const error = await response.json().catch(() => ({ error: 'Bridge unavailable' })) as { error: string }; throw new Error(error.error); }
  const text = await response.text(); return text ? JSON.parse(text) as T : undefined as T;
}
export function useBooth(customer = false) {
  const [status, setStatus] = useState<Status | null>(null);
  const [frame, setFrame] = useState('');
  const [connected, setConnected] = useState(false);
  const [count, setCount] = useState(0);
  useEffect(() => {
    let disposed = false;
    let webcamStream: MediaStream | null = null;
    let webcamVideo: HTMLVideoElement | null = null;
    let webcamCanvas: HTMLCanvasElement | null = null;
    let webcamTimer = 0;
    let webcamStarting = false;
    let webcamFramePending = false;
    function stopWebcam() {
      if (webcamTimer) window.clearTimeout(webcamTimer);
      webcamTimer = 0;
      webcamStream?.getTracks().forEach(track => track.stop());
      webcamStream = null;
      webcamVideo = null;
      webcamCanvas = null;
    }
    function sendWebcamFrame() {
      if (disposed || !webcamStream || !webcamVideo || !webcamCanvas) return;
      const video = webcamVideo; const canvas = webcamCanvas;
      if (video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA && !webcamFramePending) {
        canvas.width = Math.min(video.videoWidth, 960); canvas.height = Math.max(1, Math.round(canvas.width * video.videoHeight / video.videoWidth));
        canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height);
        webcamFramePending = true;
        void new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', .82)).then(blob => blob && fetch('/api/camera/webcam/frame', { method: 'POST', headers: { 'X-Booth-Client': '1', 'Content-Type': 'image/jpeg' }, body: blob })).catch(() => undefined).finally(() => { webcamFramePending = false; });
      }
      webcamTimer = window.setTimeout(sendWebcamFrame, 100);
    }
    async function startWebcam() {
      if (webcamStarting || webcamStream || !navigator.mediaDevices?.getUserMedia) return;
      webcamStarting = true;
      try {
        webcamStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false });
        webcamVideo = document.createElement('video'); webcamVideo.autoplay = true; webcamVideo.muted = true; webcamVideo.playsInline = true; webcamVideo.srcObject = webcamStream;
        webcamCanvas = document.createElement('canvas');
        await webcamVideo.play();
        sendWebcamFrame();
      } catch { stopWebcam(); }
      finally { webcamStarting = false; }
    }
    function syncWebcam(value: Status) {
      if (value.camera.provider === 'webcam' && value.camera.connected && value.camera.liveView) void startWebcam();
      else stopWebcam();
    }
    const connection = new HubConnectionBuilder().withUrl('/hubs/booth').withAutomaticReconnect([0, 1000, 3000, 5000]).configureLogging(LogLevel.Error).build();
    const refresh = async () => { try { const value = await api<Status>('/system/status'); if (!disposed) { setStatus(value); syncWebcam(value); } } catch { if (!disposed) setConnected(false); } };
    for (const event of ['systemStatusChanged', 'sessionChanged', 'cloudUploadFinished']) connection.on(event, (s: Status) => { setStatus(s); setCount(s.countdown); syncWebcam(s); if (!s.camera.liveView) setFrame(''); });
    connection.on('liveViewFrame', (jpeg: string) => setFrame('data:image/jpeg;base64,' + jpeg));
    connection.on('countdownTick', (value: number) => setCount(value));
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(async () => { setConnected(true); if (customer) await connection.invoke('RegisterCustomer'); await refresh(); });
    connection.onclose(() => { if (!disposed) { setConnected(false); setTimeout(() => void start(), 3000); } });
    async function start() { if (disposed) return; try { await connection.start(); if (disposed) { await connection.stop(); return; } setConnected(true); if (customer) await connection.invoke('RegisterCustomer'); await refresh(); } catch { if (!disposed) setTimeout(() => void start(), 3000); } }
    void start(); return () => { disposed = true; stopWebcam(); void connection.stop(); };
  }, [customer]);
  return { status, frame, connected, count };
}
export async function openCustomerDisplay() {
  const popup = window.open('/booth', 'vinhuni-booth', 'popup,width=1280,height=800');
  if (!popup) throw new Error('Cho phép popup để mở màn hình khách.');
  if ('getScreenDetails' in window) {
    try {
      const details = await (window as Window & { getScreenDetails: () => Promise<{ screens: { isPrimary: boolean; left: number; top: number; width: number; height: number }[] }> }).getScreenDetails();
      const external = details.screens.find(s => !s.isPrimary);
      if (external) { popup.moveTo(external.left, external.top); popup.resizeTo(external.width, external.height); }
    } catch { /* The popup stays usable when monitor access is denied. */ }
  }
  return 'Đã mở màn hình khách. Kéo sang Display 2 nếu cần, bấm Toàn màn hình hoặc F11.';
}
