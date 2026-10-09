namespace Booth.Camera;
public interface ICameraProvider : IAsyncDisposable
{
    event Action<byte[]>? LiveViewFrameReceived;
    event Action<string>? PhotoCaptured;
    event Action? CameraConnected;
    event Action? CameraDisconnected;
    event Action<string>? CameraError;
    Task InitializeAsync(); Task ConnectAsync(); Task DisconnectAsync(); Task StartLiveViewAsync(); Task StopLiveViewAsync();
    Task<string> CaptureAsync(string destination, CancellationToken cancellationToken);
    Task<CameraStatus> GetStatusAsync();
    Task<IReadOnlyList<CameraDevice>> ScanAsync();
}
