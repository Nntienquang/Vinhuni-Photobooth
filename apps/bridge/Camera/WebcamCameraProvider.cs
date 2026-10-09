using SkiaSharp;

namespace Booth.Camera;

public sealed class WebcamCameraProvider : ICameraProvider
{
    const int CaptureWidth = 1800;
    const int CaptureHeight = 1200;
    readonly object sync = new();
    byte[]? latestFrame;
    bool connected;
    bool liveView;

    public event Action<byte[]>? LiveViewFrameReceived;
    public event Action<string>? PhotoCaptured;
    public event Action? CameraConnected;
    public event Action? CameraDisconnected;
    public event Action<string>? CameraError;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task<IReadOnlyList<CameraDevice>> ScanAsync() => Task.FromResult<IReadOnlyList<CameraDevice>>([new("webcam", "webcam", "Browser", "Webcam", "MediaDevices")]);
    public Task ConnectAsync()
    {
        connected = true;
        CameraConnected?.Invoke();
        return Task.CompletedTask;
    }
    public Task DisconnectAsync()
    {
        liveView = false;
        connected = false;
        lock (sync) latestFrame = null;
        CameraDisconnected?.Invoke();
        return Task.CompletedTask;
    }
    public Task<CameraStatus> GetStatusAsync() => Task.FromResult(new CameraStatus(connected, liveView, "webcam", "Webcam", Manufacturer: "Browser", ConnectionType: "MediaDevices"));
    public Task StartLiveViewAsync()
    {
        if (!connected) throw new InvalidOperationException("Webcam disconnected");
        liveView = true;
        return Task.CompletedTask;
    }
    public Task StopLiveViewAsync()
    {
        liveView = false;
        return Task.CompletedTask;
    }
    public void SubmitFrame(byte[] jpeg)
    {
        if (!connected || !liveView) return;
        if (jpeg.Length is < 4 or > 4 * 1024 * 1024 || jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[^2] != 0xff || jpeg[^1] != 0xd9)
        {
            CameraError?.Invoke("Expected a valid webcam JPEG");
            throw new ArgumentException("Expected a valid webcam JPEG");
        }
        lock (sync) latestFrame = [.. jpeg];
        LiveViewFrameReceived?.Invoke(jpeg);
    }
    public Task<string> CaptureAsync(string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connected) throw new InvalidOperationException("Webcam disconnected");
        byte[] frame;
        lock (sync) frame = latestFrame is null ? throw new InvalidOperationException("Webcam frame unavailable. Allow camera access and start live view.") : [.. latestFrame];
        using var source = SKBitmap.Decode(frame) ?? throw new InvalidDataException("Webcam frame was not a valid JPEG");
        var sourceRatio = (double)source.Width / source.Height;
        var targetRatio = (double)CaptureWidth / CaptureHeight;
        SKRectI crop;
        if (sourceRatio > targetRatio)
        {
            var width = (int)Math.Round(source.Height * targetRatio);
            crop = new((source.Width - width) / 2, 0, (source.Width + width) / 2, source.Height);
        }
        else
        {
            var height = (int)Math.Round(source.Width / targetRatio);
            crop = new(0, (source.Height - height) / 2, source.Width, (source.Height + height) / 2);
        }
        using var output = new SKBitmap(CaptureWidth, CaptureHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(output))
        using (var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            canvas.DrawBitmap(source, crop, new SKRect(0, 0, CaptureWidth, CaptureHeight), paint);
        using var encoded = output.Encode(SKEncodedImageFormat.Jpeg, 92);
        cancellationToken.ThrowIfCancellationRequested();
        Storage.Atomic(destination, encoded.ToArray());
        PhotoCaptured?.Invoke(destination);
        return Task.FromResult(destination);
    }
    public ValueTask DisposeAsync() => new(DisconnectAsync());
}