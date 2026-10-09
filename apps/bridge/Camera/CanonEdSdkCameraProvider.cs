namespace Booth.Camera;
public sealed class CanonEdSdkCameraProvider : ICameraProvider
{
    readonly CanonEdSdkService sdk = new();
    public Task<IReadOnlyList<CameraDevice>> ScanAsync() => sdk.Scan();
    public event Action<byte[]>? LiveViewFrameReceived; public event Action<string>? PhotoCaptured; public event Action? CameraConnected; public event Action? CameraDisconnected; public event Action<string>? CameraError;
    public CanonEdSdkCameraProvider()
    {
        sdk.Frame += b => LiveViewFrameReceived?.Invoke(b);
        sdk.Photo += p => PhotoCaptured?.Invoke(p);
        sdk.Connected += () => CameraConnected?.Invoke();
        sdk.Disconnected += () => CameraDisconnected?.Invoke();
        sdk.Error += e => CameraError?.Invoke(e);
    }
    public Task InitializeAsync() => sdk.Initialize(); public Task ConnectAsync() => sdk.Connect(); public Task DisconnectAsync() => sdk.Disconnect(); public Task StartLiveViewAsync() => sdk.StartLive(); public Task StopLiveViewAsync() => sdk.StopLive(); public Task<string> CaptureAsync(string destination, CancellationToken cancellationToken) => sdk.Capture(destination, cancellationToken); public Task<CameraStatus> GetStatusAsync() => sdk.Status(); public ValueTask DisposeAsync() => sdk.DisposeAsync();
}
