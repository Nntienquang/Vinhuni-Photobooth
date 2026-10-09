using Booth;
using Booth.Camera;
using SkiaSharp;
using Xunit;

namespace Bridge.Tests;

public sealed class CameraTests
{
    static string PreferencesFile() => Path.Combine(Path.GetTempPath(), "booth-camera-" + Guid.NewGuid(), "camera.json");
    static RuntimeCameraProvider Manager(string path, Func<string, ICameraProvider> factory, string provider = "auto")
    {
        Storage.WriteJson(path, new CameraPreferences(provider));
        return new(path, factory);
    }
    [Theory]
    [InlineData("Canon EOS R50 V", true)] [InlineData("Canon EOS RP", true)]
    [InlineData("EOS RP", true)] [InlineData("Canon EOS R50", false)] [InlineData("Canon EOS R5", false)]
    public void CanonModels(string model, bool supported) => Assert.Equal(supported, CanonEdSdkService.IsSupportedModel(model));

    [Fact]
    public async Task SwitchDisposesOldProviderAndPersistsSelection()
    {
        var path = PreferencesFile(); var providers = new List<FakeCamera>();
        await using var manager = Manager(path, name => { var p = new FakeCamera(name); providers.Add(p); return p; }, "mock");
        await manager.ConnectAsync();
        await manager.SwitchAsync(new("canon", false, false));
        Assert.True(providers[0].Disposed); Assert.Equal(new[] { "stop", "disconnect", "dispose" }, providers[0].Calls.TakeLast(3));
        Assert.Equal("canon", (await manager.GetStatusAsync()).Provider);
        Assert.False((await manager.GetStatusAsync()).LiveView);
        await using var reloaded = new RuntimeCameraProvider(path, name => new FakeCamera(name));
        Assert.Equal(new CameraPreferences("canon", false, false), reloaded.Preferences);
    }
    [Theory]
    [InlineData(BoothState.FRAME_SELECTION)] [InlineData(BoothState.COUNTDOWN)] [InlineData(BoothState.CAPTURING)] [InlineData(BoothState.FINAL_PREVIEW)]
    public void ActiveStatesBlockSwitch(BoothState state)
    {
        Assert.Equal("Cannot change camera while a photo session is active.", Assert.Throws<InvalidOperationException>(() => BoothEngine.RequireCameraIdle(state, null)).Message);
    }
    [Fact]
    public void PausingDoesNotBypassActiveSessionGuard()
    {
        var session = new Session { Template = new("t", "t", 100, 100, 300, 1, "portrait", true, 0, []), Shots = ["saved.jpg"] };
        Assert.Throws<InvalidOperationException>(() => BoothEngine.RequireCameraIdle(BoothState.MAINTENANCE, session));
        Assert.Equal("saved.jpg", Assert.Single(session.Shots));
        BoothEngine.RequireCameraIdle(BoothState.IDLE, null); BoothEngine.RequireCameraIdle(BoothState.MAINTENANCE, null);
    }
    [Fact]
    public async Task DisconnectReconnectAndForwardEvents()
    {
        FakeCamera? active = null;
        await using var manager = Manager(PreferencesFile(), name => active = new FakeCamera(name), "mock");
        var disconnects = 0; manager.CameraDisconnected += () => disconnects++;
        await manager.ConnectAsync(); active!.Unplug();
        Assert.False((await manager.GetStatusAsync()).Connected); Assert.Equal(1, disconnects);
        await manager.ReconnectAsync(); Assert.True((await manager.GetStatusAsync()).Connected);
        await manager.DisconnectAsync(); Assert.False((await manager.GetStatusAsync()).Connected);
        await manager.ConnectAsync(); Assert.True((await manager.GetStatusAsync()).Connected);
    }
    [Theory] [InlineData(true, "canon")] [InlineData(false, "sony")]
    public async Task AutoDetectPrefersCanonThenSony(bool canonAvailable, string expected)
    {
        var created = new List<string>();
        await using var manager = Manager(PreferencesFile(), name => { created.Add(name); return new FakeCamera(name, name == "sony" || canonAvailable); });
        await manager.ConnectAsync(); Assert.Equal(expected, (await manager.GetStatusAsync()).Provider);
        Assert.DoesNotContain("mock", created); if (canonAvailable) Assert.DoesNotContain("sony", created);
    }
    [Fact]
    public async Task AutoNeverFallsBackToMock()
    {
        var created = new List<string>();
        await using var manager = Manager(PreferencesFile(), name => { created.Add(name); return new FakeCamera(name, false); });
        await manager.ConnectAsync(); Assert.False((await manager.GetStatusAsync()).Connected); Assert.DoesNotContain("mock", created);
        await manager.SwitchAsync(new("mock")); Assert.True((await manager.GetStatusAsync()).Connected);
    }
    [Fact]
    public async Task SonyMissingDependencyCannotFakeCaptureOrConnection()
    {
        await using var sony = new SonyCameraProvider(new FakeSonyTransport { Failure = new System.Runtime.InteropServices.COMException("WPD missing", unchecked((int)0x80040154)) });
        await Assert.ThrowsAsync<System.Runtime.InteropServices.COMException>(sony.ScanAsync);
        Assert.Equal("DependencyMissing", (await sony.GetStatusAsync()).Availability);
        await Assert.ThrowsAsync<InvalidOperationException>(sony.ConnectAsync);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sony.CaptureAsync(file, CancellationToken.None));
        Assert.False(File.Exists(file)); Assert.False((await sony.GetStatusAsync()).Connected);
    }
    [Fact]
    public async Task FailedSelectionIsPersistedAndCanRecoverToMock()
    {
        await using var manager = Manager(PreferencesFile(), name => name == "sony" ? new SonyCameraProvider(new FakeSonyTransport { Failure = new System.Runtime.InteropServices.COMException("WPD missing", unchecked((int)0x80040154)) }) : new FakeCamera(name), "mock");
        await manager.ConnectAsync();
        await Assert.ThrowsAsync<System.Runtime.InteropServices.COMException>(() => manager.SwitchAsync(new("sony")));
        Assert.Equal("sony", manager.Preferences.Provider); Assert.Equal("DependencyMissing", (await manager.GetStatusAsync()).Availability);
        await manager.SwitchAsync(new("mock")); Assert.True((await manager.GetStatusAsync()).Connected);
    }
    [Fact]
    public async Task WebcamCanBeSelectedExplicitly()
    {
        await using var manager = Manager(PreferencesFile(), name => name == "webcam" ? new WebcamCameraProvider() : new MockCameraProvider(), "mock");
        await manager.SwitchAsync(new("webcam", false, true));
        Assert.Equal("webcam", (await manager.GetStatusAsync()).Provider);
        Assert.Equal("Webcam", (await manager.ScanAsync()).Single().Model);
    }
    [Fact]
    public async Task WebcamCaptureNormalizesToHorizontalThreeByTwo()
    {
        await using var camera = new WebcamCameraProvider();
        await camera.ConnectAsync(); await camera.StartLiveViewAsync(); camera.SubmitFrame(CreateJpeg(2000, 1000));
        var path = Path.Combine(Path.GetTempPath(), "webcam-normalized-" + Guid.NewGuid() + ".jpg");
        try { await camera.CaptureAsync(path, CancellationToken.None); using var image = SKBitmap.Decode(path); Assert.Equal(1800, image!.Width); Assert.Equal(1200, image.Height); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public async Task WebcamCaptureMatchesExistingPhotoCapturedContract()
    {
        await using var camera = new WebcamCameraProvider(); string? captured = null; camera.PhotoCaptured += path => captured = path;
        await camera.ConnectAsync(); await camera.StartLiveViewAsync(); camera.SubmitFrame(CreateJpeg(900, 600));
        var path = Path.Combine(Path.GetTempPath(), "webcam-captured-" + Guid.NewGuid() + ".jpg");
        try { Assert.Equal(path, await camera.CaptureAsync(path, CancellationToken.None)); Assert.Equal(path, captured); Assert.True(File.ReadAllBytes(path)[0..2].SequenceEqual(new byte[] { 0xff, 0xd8 })); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public async Task WebcamCaptureWritesThroughSessionStoragePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "booth-webcam-session-" + Guid.NewGuid()); var previous = Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR", root); var storage = new Storage(); var session = new Session { Template = new("test", "Test", 1200, 1800, 300, 1, "portrait", true, 0, []) }; var path = Path.Combine(storage.SessionDir(session), "originals", "take_0", "shot_01.jpg");
            await using var camera = new WebcamCameraProvider(); await camera.ConnectAsync(); await camera.StartLiveViewAsync(); camera.SubmitFrame(CreateJpeg(1200, 800)); await camera.CaptureAsync(path, CancellationToken.None); session.Shots = [path]; storage.Save(session);
            Assert.True(File.Exists(path)); Assert.Equal(path, storage.Sessions().Single().Shots.Single());
        }
        finally { Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR", previous); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.CadetBlue); using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90); return data.ToArray();
    }
    sealed class FakeCamera(string name, bool available = true) : ICameraProvider
    {
        public List<string> Calls { get; } = [];
        public bool Disposed; bool connected, live;
        public event Action<byte[]>? LiveViewFrameReceived { add { } remove { } }
        public event Action<string>? PhotoCaptured { add { } remove { } }
        public event Action? CameraConnected;
        public event Action? CameraDisconnected;
        public event Action<string>? CameraError { add { } remove { } }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<CameraDevice>> ScanAsync() => Task.FromResult<IReadOnlyList<CameraDevice>>(available ? [new(name, name, name, name, "USB")] : []);
        public Task ConnectAsync() { connected = true; CameraConnected?.Invoke(); return Task.CompletedTask; }
        public Task DisconnectAsync() { Calls.Add("disconnect"); Unplug(); return Task.CompletedTask; }
        public void Unplug() { connected = live = false; CameraDisconnected?.Invoke(); }
        public Task StartLiveViewAsync() { live = true; return Task.CompletedTask; }
        public Task StopLiveViewAsync() { Calls.Add("stop"); live = false; return Task.CompletedTask; }
        public Task<string> CaptureAsync(string destination, CancellationToken cancellationToken) => Task.FromResult(destination);
        public Task<CameraStatus> GetStatusAsync() => Task.FromResult(new CameraStatus(connected, live, name, name));
        public ValueTask DisposeAsync() { Calls.Add("dispose"); Disposed = true; return ValueTask.CompletedTask; }
    }
}
