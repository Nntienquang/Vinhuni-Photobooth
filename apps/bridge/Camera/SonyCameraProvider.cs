using Booth.Camera.Sony;
using System.Runtime.InteropServices;
using Serilog;
namespace Booth.Camera;

public sealed class SonyCameraProvider : ICameraProvider
{
    readonly ISonyTransport transport;
    readonly SemaphoreSlim operation = new(1, 1);
    readonly SemaphoreSlim captureGate = new(1, 1);
    readonly TimeSpan timeout;
    CancellationTokenSource? liveCancel, captureCancel;
    Task liveTask = Task.CompletedTask;
    volatile bool connected;
    bool disposed;
    string state = "Disconnected";
    string? error;
    CameraDevice? device;
    public event Action<byte[]>? LiveViewFrameReceived;
    public event Action<string>? PhotoCaptured;
    public event Action? CameraConnected;
    public event Action? CameraDisconnected;
    public event Action<string>? CameraError;
    public SonyCameraProvider() : this(new SonyWpdTransport()) { }
    public SonyCameraProvider(ISonyTransport transport, TimeSpan? captureTimeout = null) { this.transport = transport; timeout = captureTimeout ?? TimeSpan.FromSeconds(23); }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task<IReadOnlyList<CameraDevice>> ScanAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { var devices = await transport.ScanAsync(deadline.Token); if (!connected) device = devices.FirstOrDefault(); foreach (var found in devices) Log.Information("Sony device discovered: {Model}", found.Model); return devices; }
        catch (Exception e) { Report(e); throw; }
    }
    public async Task ConnectAsync()
    {
        await operation.WaitAsync();
        try
        {
            if (connected) return; state = "Connecting"; error = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            device = (await transport.ScanAsync(deadline.Token)).FirstOrDefault() ?? throw new IOException("Sony ILCE-7M3 not detected. Check USB data cable and USB Connection=PC Remote.");
            await transport.OpenAsync(device, deadline.Token); device = await transport.GetDeviceInfoAsync(deadline.Token);
            connected = true; state = "Connected"; Log.Information("Sony connection opened: {Model}", device.Model); CameraConnected?.Invoke();
        }
        catch (Exception e) { connected = false; await transport.CloseAsync(); Report(e); throw new InvalidOperationException(error, e); }
        finally { operation.Release(); }
    }
    public async Task DisconnectAsync()
    {
        captureCancel?.Cancel(); await StopLiveViewAsync(); await operation.WaitAsync();
        try { await transport.CloseAsync(); bool wasConnected = connected; connected = false; state = "Disconnected"; error = null; Log.Information("Sony device disconnected"); if (wasConnected) CameraDisconnected?.Invoke(); }
        finally { operation.Release(); }
    }
    public async Task StartLiveViewAsync()
    {
        await operation.WaitAsync();
        try { RequireConnected(); if (liveCancel is not null) return; using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await transport.StartLiveViewAsync(deadline.Token); liveCancel = new(); state = "LiveView"; liveTask = Task.Run(() => LiveLoop(liveCancel.Token)); Log.Information("Sony live view started"); }
        finally { operation.Release(); }
    }
    async Task LiveLoop(CancellationToken token)
    {
        try
        {
            long lastFrame = Environment.TickCount64;
            while (!token.IsCancellationRequested)
            {
                long started = Environment.TickCount64;
                if (await operation.WaitAsync(0, token))
                {
                    try { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5)); var frame = await transport.ReadLiveViewFrameAsync(deadline.Token); if (frame is not null) { lastFrame = Environment.TickCount64; LiveViewFrameReceived?.Invoke(frame); } else if (Environment.TickCount64 - lastFrame > 5000) throw new IOException("Sony live view timed out. Check USB/PC Remote mode and reconnect."); }
                    finally { operation.Release(); }
                }
                else lastFrame = Environment.TickCount64; // Capture owns the transport temporarily.
                await Task.Delay((int)Math.Max(1, 100 - (Environment.TickCount64 - started)), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { connected = false; state = "Disconnected"; error = Explain(e); captureCancel?.Cancel(); Log.Warning("Sony device disconnected: {Error}", error); CameraDisconnected?.Invoke(); CameraError?.Invoke(error); }
    }
    public async Task StopLiveViewAsync()
    {
        var cancel = liveCancel; if (cancel is null) return;
        cancel.Cancel(); await liveTask; liveCancel = null; cancel.Dispose();
        await operation.WaitAsync(); try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await transport.StopLiveViewAsync(deadline.Token); if (connected) state = "Connected"; } finally { operation.Release(); }
    }
    public async Task<string> CaptureAsync(string destination, CancellationToken cancellationToken)
    {
        RequireConnected();
        if (!await captureGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Sony camera is busy capturing");
        bool acquired = false;
        captureCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); captureCancel.CancelAfter(timeout);
        try
        {
            state = "Busy";
            await operation.WaitAsync(captureCancel.Token); acquired = true;
            RequireConnected(); var token = captureCancel!.Token;
            state = "Capturing"; Log.Information("Sony capture requested");
            var photo = await transport.CaptureAsync(token); Log.Information("Sony shutter completed");
            state = "Downloading"; Log.Information("Sony JPEG downloading: {Bytes}", photo.Size);
            var jpeg = await transport.DownloadCapturedJpegAsync(photo, token); token.ThrowIfCancellationRequested();
            using var image = SkiaSharp.SKBitmap.Decode(jpeg);
            if (image is null || jpeg.Length < 4 || jpeg[0] != 255 || jpeg[1] != 216) throw new InvalidDataException("Sony capture did not contain a valid full JPEG");
            Storage.Atomic(destination, jpeg); Log.Information("Sony JPEG saved: {Path}", destination); PhotoCaptured?.Invoke(destination); state = liveCancel is null ? "Connected" : "LiveView"; return destination;
        }
        catch (Exception e)
        {
            connected = false; liveCancel?.Cancel(); await transport.CloseAsync();
            Report(e is OperationCanceledException && !cancellationToken.IsCancellationRequested ? new TimeoutException("Sony capture timed out. Check focus, JPEG/PC Remote save settings, then reconnect USB before retrying.", e) : e);
            CameraDisconnected?.Invoke();
            if (e is OperationCanceledException && !cancellationToken.IsCancellationRequested) throw new TimeoutException(error, e);
            throw;
        }
        finally { captureCancel?.Dispose(); captureCancel = null; if (acquired) operation.Release(); captureGate.Release(); }
    }
    void RequireConnected() { if (!connected) throw new InvalidOperationException("Sony camera disconnected. CONNECT or RECONNECT first."); }
    void Report(Exception e) { error = Explain(e); state = e is COMException { HResult: unchecked((int)0x80040154) } or DllNotFoundException ? "DependencyMissing" : e is IOException ? "Disconnected" : "Error"; CameraError?.Invoke(error); Log.Warning("Sony camera: {Error}", error); }
    static string Explain(Exception e) => e is COMException && e.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x800700AA)
        ? "Camera is currently in use by another application. Close Sony Imaging Edge Remote and try again."
        : e is COMException { HResult: unchecked((int)0x80040154) } ? "Windows Portable Devices COM components are missing. Enable Windows Media Feature Pack on Windows N and repair the Windows WPD installation. See docs/SONY_SETUP.md." : e.Message;
    public Task<CameraStatus> GetStatusAsync() => Task.FromResult(new CameraStatus(connected, connected && liveCancel is { IsCancellationRequested: false }, "sony", device?.Model ?? "No camera detected", error, "Sony", "USB / PC Remote", state));
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; try { await DisconnectAsync(); } finally { await transport.DisposeAsync(); operation.Dispose(); captureGate.Dispose(); } }
}
