import { useState } from 'react';
import type { CameraDevice, CameraPreferences, Status } from '@booth/shared';
import { api } from '../api';

export function CameraSettings({ status, notify }: { status: Status; notify: (message: string) => void }) {
  const [busy, setBusy] = useState(false);
  const [devices, setDevices] = useState<CameraDevice[]>([]);
  const preferences = status.cameraPreferences ?? { provider: 'auto', autoConnect: true, startLiveView: true };
  const blocked = !!status.session || !['IDLE', 'MAINTENANCE'].includes(status.state);
  async function run(action: string, body?: CameraPreferences) {
    setBusy(true);
    try {
      if (action === 'scan') {
        const found = await api<CameraDevice[]>('/camera/scan', 'POST');
        setDevices(found); notify(found.length ? `Detected ${found.map(d => d.model).join(', ')}` : 'No supported camera detected. Check camera status below.');
      } else if (action === 'capture') {
        const result = await api<{ localJpegPath: string }>('/camera/capture', 'POST'); notify(`Test capture saved: ${result.localJpegPath}`);
      } else {
        await api('/camera/' + action, action === 'preferences' ? 'PUT' : 'POST', body);
        if (action === 'provider') setDevices([]);
        notify(action === 'live-view/start' ? 'Live view started. Open Dashboard to view the camera monitor.' : 'Camera settings updated. Check device status below.');
      }
    } catch (error) { notify(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  }
  return <div>
    {blocked && <p role="status">Cannot change camera while a photo session is active.</p>}
    <label>Camera Provider<select aria-label="Camera Provider" disabled={busy || blocked} value={preferences.provider} onChange={e => void run('provider', { ...preferences, provider: e.target.value })}>
      <option value="auto">Auto Detect</option><option value="canon">Canon EDSDK</option><option value="sony">Sony</option><option value="mock">Mock</option><option value="webcam">Webcam (Development)</option>
    </select></label>
    <label className="toggle"><span>Auto-connect camera on startup</span><input type="checkbox" disabled={busy || blocked} checked={preferences.autoConnect} onChange={e => void run('preferences', { ...preferences, autoConnect: e.target.checked })} /></label>
    <label className="toggle"><span>Start live view on connect</span><input type="checkbox" disabled={busy || blocked} checked={preferences.startLiveView} onChange={e => void run('preferences', { ...preferences, startLiveView: e.target.checked })} /></label>
    <dl><dt>Detected Camera / Model</dt><dd>{status.camera.model}</dd><dt>Manufacturer</dt><dd>{status.camera.manufacturer || '—'}</dd><dt>Connection Type</dt><dd>{status.camera.connectionType || '—'}</dd><dt>Status</dt><dd>{status.camera.availability ?? (status.camera.connected ? 'Connected' : 'Disconnected')}{status.camera.liveView ? ' / LIVE' : ''}</dd></dl>
    {status.camera.error && <p className="inline-error" role="alert">{status.camera.error}</p>}
    {devices.map(d => <p key={d.provider + d.id}>{d.manufacturer} · {d.model} · {d.connectionType}</p>)}
    <div className="camera-actions">{[['scan', 'SCAN DEVICES'], ['connect', 'CONNECT'], ['disconnect', 'DISCONNECT'], ['reconnect', 'RECONNECT'], ['live-view/start', 'TEST LIVE VIEW'], ['capture', 'TEST CAPTURE']].map(([action, label]) => <button key={action} className="secondary" disabled={busy || (action !== 'disconnect' && (['connect', 'reconnect'].includes(action) ? !['IDLE', 'MAINTENANCE'].includes(status.state) : blocked))} onClick={() => void run(action)}>{label}</button>)}</div>
  </div>;
}
