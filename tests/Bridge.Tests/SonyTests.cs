using Booth;
using Booth.Camera;
using Booth.Camera.Sony;
using SkiaSharp;
using Xunit;

namespace Bridge.Tests;

public sealed class SonyTests
{
    [Fact]
    public async Task DiscoveryIsPhysicalAndConnectionCanRecover()
    {
        var transport = new FakeSonyTransport { Present = false };
        await using var camera = new SonyCameraProvider(transport);
        Assert.Empty(await camera.ScanAsync());
        Assert.Equal("Disconnected", (await camera.GetStatusAsync()).Availability);
        await Assert.ThrowsAsync<InvalidOperationException>(camera.ConnectAsync);
        transport.Present = true;
        Assert.Equal("Sony A7 III / ILCE-7M3", Assert.Single(await camera.ScanAsync()).Model);
        await camera.ConnectAsync(); Assert.True((await camera.GetStatusAsync()).Connected);
        await camera.DisconnectAsync(); Assert.False((await camera.GetStatusAsync()).Connected);
        await camera.ConnectAsync(); Assert.True((await camera.GetStatusAsync()).Connected);
        Assert.Equal(2, transport.Opens);
    }

    [Fact]
    public async Task LiveFramesStopAndFullStillIsDownloadedWithoutReencoding()
    {
        var transport = new FakeSonyTransport();
        await using var camera = new SonyCameraProvider(transport);
        var frame = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        camera.LiveViewFrameReceived += bytes => frame.TrySetResult(bytes);
        await camera.ConnectAsync(); await camera.StartLiveViewAsync();
        Assert.Equal(transport.Preview, await frame.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        string? saved = null; camera.PhotoCaptured += p => saved = p;
        try
        {
            Assert.Equal(path, await camera.CaptureAsync(path, CancellationToken.None));
            Assert.Equal(path, saved); Assert.Equal(transport.Jpeg, await File.ReadAllBytesAsync(path));
            Assert.Equal(1, transport.Downloads); Assert.NotEqual(transport.Preview, transport.Jpeg);
        }
        finally { File.Delete(path); }
        await camera.StopLiveViewAsync(); Assert.False((await camera.GetStatusAsync()).LiveView);
        Assert.False(transport.Live);
    }

    [Fact]
    public async Task TimeoutIsBoundedAndCannotSavePreview()
    {
        var transport = new FakeSonyTransport { HoldCapture = true };
        await using var camera = new SonyCameraProvider(transport, TimeSpan.FromMilliseconds(80));
        await camera.ConnectAsync();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        var error = await Assert.ThrowsAsync<TimeoutException>(() => camera.CaptureAsync(path, CancellationToken.None));
        Assert.Contains("timed out", error.Message); Assert.False(File.Exists(path)); Assert.Equal(0, transport.Downloads);
        Assert.False((await camera.GetStatusAsync()).Connected);
    }

    [Fact]
    public async Task DuplicateCaptureRejectedAndDisconnectCancelsPendingCapture()
    {
        var transport = new FakeSonyTransport { HoldCapture = true };
        await using var camera = new SonyCameraProvider(transport);
        await camera.ConnectAsync();
        using var cancelled = new CancellationTokenSource();
        var capture = camera.CaptureAsync("unused.jpg", cancelled.Token);
        await transport.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => camera.CaptureAsync("duplicate.jpg", CancellationToken.None));
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        await camera.DisconnectAsync(); Assert.False((await camera.GetStatusAsync()).Connected);
        transport.HoldCapture = false; await camera.ConnectAsync(); Assert.True((await camera.GetStatusAsync()).Connected);
    }

    [Fact]
    public async Task UnplugDuringDownloadDoesNotSaveFileAndReconnectWorks()
    {
        var transport = new FakeSonyTransport { FailDownload = true };
        await using var camera = new SonyCameraProvider(transport); await camera.ConnectAsync();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        await Assert.ThrowsAsync<IOException>(() => camera.CaptureAsync(path, CancellationToken.None));
        Assert.False(File.Exists(path)); Assert.Equal("Disconnected", (await camera.GetStatusAsync()).Availability);
        transport.FailDownload = false; await camera.ConnectAsync(); Assert.True((await camera.GetStatusAsync()).Connected);
    }

    [Fact]
    public async Task SonyMockSonyReleasesRealProviderBoundary()
    {
        var created = new List<FakeSonyTransport>();
        var path = Path.Combine(Path.GetTempPath(), "sony-switch-" + Guid.NewGuid(), "camera.json");
        Storage.WriteJson(path, new CameraPreferences("sony", true, false));
        await using var runtime = new RuntimeCameraProvider(path, name =>
        {
            if (name == "mock") return new MockCameraProvider();
            var transport = new FakeSonyTransport(); created.Add(transport); return new SonyCameraProvider(transport);
        });
        await runtime.ConnectAsync(); await runtime.SwitchAsync(new("mock", true, false));
        Assert.True(created[0].Disposed); Assert.Equal("mock", (await runtime.GetStatusAsync()).Provider);
        await runtime.SwitchAsync(new("sony", true, false));
        Assert.True((await runtime.GetStatusAsync()).Connected); Assert.Equal(2, created.Count);
    }

    [Fact]
    public void PreviewUsesHeaderOffsetAndRejectsTruncation()
    {
        byte[] payload = [8, 0, 0, 0, 255, 216, 0, 0, 255, 216, 42, 255, 217, 0];
        Assert.Equal(new byte[] { 255, 216, 42, 255, 217 }, SonyWpdTransport.ExtractPreview(payload));
        Assert.Throws<InvalidDataException>(() => SonyWpdTransport.ExtractPreview([255, 255, 255, 255, 0, 0]));
        Assert.Throws<InvalidDataException>(() => SonyWpdTransport.ExtractPreview(payload[..11]));
    }

    [Fact]
    public void PropertyParserSkipsEnumerationAndReadsPendingImage()
    {
        byte[] data = [2,0,0,0,0,0,0,0, 0x18,0xd2,2,0,0,1, 0,75,2,2,0,0,100, 0x15,0xd2,4,0,0,1, 0,0,1,0x80,0];
        Assert.Equal(0x8001UL, SonyProperties.Read(data, 0xd215));
        Assert.Throws<InvalidDataException>(() => SonyProperties.Read(data[..^3], 0xd215));
    }
}

internal sealed class FakeSonyTransport : ISonyTransport
{
    public bool Present = true, Live, Disposed, HoldCapture, FailDownload;
    public Exception? Failure;
    public int Opens, Downloads;
    public TaskCompletionSource CaptureStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public byte[] Preview = [255, 216, 12, 255, 217];
    public byte[] Jpeg { get; } = MakeJpeg();
    static readonly CameraDevice Device = new("fake-usb", "sony", "Sony", "Sony A7 III / ILCE-7M3", "USB / PC Remote");
    static byte[] MakeJpeg() { using var bitmap = new SKBitmap(100, 80); bitmap.Erase(SKColors.Blue); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90); return data.ToArray(); }
    public Task<IReadOnlyList<CameraDevice>> ScanAsync(CancellationToken token) => Failure is null ? Task.FromResult<IReadOnlyList<CameraDevice>>(Present ? [Device] : []) : Task.FromException<IReadOnlyList<CameraDevice>>(Failure);
    public Task OpenAsync(CameraDevice device, CancellationToken token) { Opens++; return Task.CompletedTask; }
    public Task CloseAsync() { Live = false; return Task.CompletedTask; }
    public Task<CameraDevice> GetDeviceInfoAsync(CancellationToken token) => Task.FromResult(Device);
    public Task StartLiveViewAsync(CancellationToken token) { Live = true; return Task.CompletedTask; }
    public Task StopLiveViewAsync(CancellationToken token) { Live = false; return Task.CompletedTask; }
    public Task<byte[]?> ReadLiveViewFrameAsync(CancellationToken token) => Task.FromResult<byte[]?>(Live ? Preview : null);
    public Task AutoFocusAsync(CancellationToken token) => Task.CompletedTask;
    public async Task<SonyCapturedObject> CaptureAsync(CancellationToken token) { CaptureStarted.TrySetResult(); if (HoldCapture) await Task.Delay(Timeout.Infinite, token); return new(0xffffc001, (uint)Jpeg.Length, "DSC00001.JPG"); }
    public Task<byte[]> DownloadCapturedJpegAsync(SonyCapturedObject photo, CancellationToken token) { Downloads++; return FailDownload ? Task.FromException<byte[]>(new IOException("USB disconnected")) : Task.FromResult(Jpeg); }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
