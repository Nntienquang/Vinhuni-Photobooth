using System.Text.Json;

namespace Booth.Camera;

/// <summary>Stable camera identity for the booth. All operations and ownership changes are serialized.</summary>
public sealed class RuntimeCameraProvider : ICameraProvider
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Func<string, ICameraProvider> factory;
    readonly string preferencesPath;
    ICameraProvider? current;
    string? error;
    bool disposed;
    public CameraPreferences Preferences { get; private set; }
    public event Action<byte[]>? LiveViewFrameReceived;
    public event Action<string>? PhotoCaptured;
    public event Action? CameraConnected;
    public event Action? CameraDisconnected;
    public event Action<string>? CameraError;
    public RuntimeCameraProvider(Storage storage) : this(Path.Combine(storage.Root, "camera-settings.json"), Create) { }
    public RuntimeCameraProvider(string path, Func<string, ICameraProvider> factory)
    {
        preferencesPath = path;
        this.factory = factory;
        Preferences = File.Exists(path) ? JsonSerializer.Deserialize<CameraPreferences>(File.ReadAllText(path), Storage.Json) ?? new() :
            new CameraPreferences(Environment.GetEnvironmentVariable("CAMERA_PROVIDER") is "mock" or "canon" or "sony" ? Environment.GetEnvironmentVariable("CAMERA_PROVIDER")! : "auto");
        Preferences.Validate();
    }
    static ICameraProvider Create(string name) => name switch { "canon" => new CanonEdSdkCameraProvider(), "sony" => new SonyCameraProvider(), "mock" => new MockCameraProvider(), "webcam" => new WebcamCameraProvider(), _ => throw new ArgumentException("Invalid provider") };
    async Task<T> Locked<T>(Func<Task<T>> action)
    {
        await gate.WaitAsync();
        try { ObjectDisposedException.ThrowIf(disposed, this); return await action(); }
        finally { gate.Release(); }
    }
    async Task Locked(Func<Task> action) => await Locked(async () => { await action(); return true; });
    void Frame(byte[] bytes) => LiveViewFrameReceived?.Invoke(bytes);
    void Photo(string path) => PhotoCaptured?.Invoke(path);
    void Connected() => CameraConnected?.Invoke();
    void Disconnected() => CameraDisconnected?.Invoke();
    void Error(string message) => CameraError?.Invoke(message);
    void Attach(ICameraProvider provider)
    {
        current = provider;
        provider.LiveViewFrameReceived += Frame; provider.PhotoCaptured += Photo;
        provider.CameraConnected += Connected; provider.CameraDisconnected += Disconnected; provider.CameraError += Error;
    }
    async Task Release()
    {
        var old = current; current = null;
        if (old is null) return;
        // Intentional teardown must not trigger a delayed disconnect against the replacement.
        old.LiveViewFrameReceived -= Frame; old.PhotoCaptured -= Photo;
        old.CameraConnected -= Connected; old.CameraDisconnected -= Disconnected; old.CameraError -= Error;
        try { await old.StopLiveViewAsync(); }
        finally { try { await old.DisconnectAsync(); } finally { await old.DisposeAsync(); } }
    }
    public Task SavePreferencesAsync(CameraPreferences preferences) => Locked(() =>
    {
        preferences.Validate();
        if (preferences.Provider != Preferences.Provider) throw new InvalidOperationException("Use provider switching to change camera");
        Storage.WriteJson(preferencesPath, preferences); Preferences = preferences;
        return Task.CompletedTask;
    });
    public Task SwitchAsync(CameraPreferences preferences) => Locked(async () =>
    {
        preferences.Validate();
        await Release();
        Storage.WriteJson(preferencesPath, preferences); Preferences = preferences;
        await ConnectCore();
    });
    public Task InitializeAsync() => Task.CompletedTask;
    public Task ConnectAsync() => Locked(ConnectCore);
    async Task ConnectCore()
    {
        error = null;
        try
        {
            if (current is null)
            {
                if (Preferences.Provider == "auto")
                {
                    var diagnostics = new List<string>();
                    foreach (var name in new[] { "canon", "sony" })
                    {
                        var candidate = factory(name);
                        try
                        {
                            await candidate.InitializeAsync();
                            var devices = await candidate.ScanAsync();
                            if (devices.Count > 0) { Attach(candidate); break; }
                            var status = await candidate.GetStatusAsync();
                            if (status.Error is not null) diagnostics.Add(status.Error);
                        }
                        catch (Exception e) { diagnostics.Add(e.Message); }
                        finally { if (current != candidate) await candidate.DisposeAsync(); }
                    }
                    if (current is null) { error = "No supported camera detected. " + string.Join(" ", diagnostics); return; }
                }
                else Attach(factory(Preferences.Provider));
            }
            if (current is null) throw new InvalidOperationException("Camera disconnected");
            await current.InitializeAsync();
            await current.ScanAsync();
            await current.ConnectAsync();
            if (Preferences.StartLiveView) await current.StartLiveViewAsync();
        }
        catch (Exception e) { error = e.Message; throw; }
    }
    public Task ReconnectAsync() => Locked(async () => { await Release(); await ConnectCore(); });
    public Task DisconnectAsync() => Locked(async () => { await Release(); error = null; });
    public Task StartLiveViewAsync() => Locked(async () => { if (current is null) throw new InvalidOperationException("Camera disconnected"); await current.StartLiveViewAsync(); });
    public Task StopLiveViewAsync() => Locked(async () => { if (current is not null) await current.StopLiveViewAsync(); });
    // The existing normalized capture contract is a local full-resolution JPEG path.
    public Task<string> CaptureAsync(string destination, CancellationToken cancellationToken) => Locked(async () =>
    {
        if (current is null) throw new InvalidOperationException("Camera disconnected");
        return await current.CaptureAsync(destination, cancellationToken);
    });
    public Task SubmitWebcamFrameAsync(byte[] jpeg) => Locked(() =>
    {
        if (current is not WebcamCameraProvider webcam) throw new InvalidOperationException("Webcam provider is not selected");
        webcam.SubmitFrame(jpeg);
        return Task.CompletedTask;
    });
    public Task<CameraStatus> GetStatusAsync() => Locked(async () =>
    {
        if (current is null) return new(false, false, Preferences.Provider, "No camera detected", error, Availability: "Disconnected");
        var status = await current.GetStatusAsync();
        return status with { Error = error ?? status.Error, Availability = status.Availability ?? (status.Connected ? "Connected" : "Disconnected"),
            Manufacturer = status.Manufacturer.Length > 0 ? status.Manufacturer : status.Provider == "canon" ? "Canon" : "Mock",
            ConnectionType = status.ConnectionType.Length > 0 ? status.ConnectionType : status.Provider == "mock" ? "Simulated" : "USB" };
    });
    public Task<IReadOnlyList<CameraDevice>> ScanAsync() => Locked<IReadOnlyList<CameraDevice>>(async () =>
    {
        if (current is not null) return await current.ScanAsync();
        var devices = new List<CameraDevice>(); var diagnostics = new List<string>();
        foreach (var name in Preferences.Provider == "auto" ? new[] { "canon", "sony" } : new[] { Preferences.Provider })
        {
            await using var candidate = factory(name);
            try { await candidate.InitializeAsync(); devices.AddRange(await candidate.ScanAsync()); var status = await candidate.GetStatusAsync(); if (status.Error is not null) diagnostics.Add(status.Error); }
            catch (Exception e) { diagnostics.Add(e.Message); }
        }
        error = diagnostics.Count > 0 ? string.Join(" ", diagnostics) : null;
        return devices;
    });
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { if (disposed) return; disposed = true; await Release(); }
        finally { gate.Release(); }
    }
}
