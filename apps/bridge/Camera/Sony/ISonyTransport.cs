namespace Booth.Camera.Sony;

public sealed record SonyCapturedObject(uint Handle, uint Size, string FileName);
public interface ISonyTransport : IAsyncDisposable
{
    Task<IReadOnlyList<CameraDevice>> ScanAsync(CancellationToken token);
    Task OpenAsync(CameraDevice device, CancellationToken token);
    Task CloseAsync();
    Task<CameraDevice> GetDeviceInfoAsync(CancellationToken token);
    Task StartLiveViewAsync(CancellationToken token);
    Task<byte[]?> ReadLiveViewFrameAsync(CancellationToken token);
    Task StopLiveViewAsync(CancellationToken token);
    Task AutoFocusAsync(CancellationToken token);
    Task<SonyCapturedObject> CaptureAsync(CancellationToken token);
    Task<byte[]> DownloadCapturedJpegAsync(SonyCapturedObject photo, CancellationToken token);
}
