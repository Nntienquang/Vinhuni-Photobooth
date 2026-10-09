using SkiaSharp;
namespace Booth.Camera;
public sealed class MockCameraProvider : ICameraProvider
{
    public event Action<byte[]>? LiveViewFrameReceived;
    public event Action<string>? PhotoCaptured;
    public event Action? CameraConnected;
    public event Action? CameraDisconnected;
    public event Action<string>? CameraError;
    bool connected; CancellationTokenSource? live; Task? loop; int frame;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task<IReadOnlyList<CameraDevice>> ScanAsync() => Task.FromResult<IReadOnlyList<CameraDevice>>([new("mock", "mock", "Mock", "Mock Camera", "Simulated")]);
    public Task ConnectAsync()
    {
        connected = true;
        CameraConnected?.Invoke();
        return Task.CompletedTask;
    }
    public async Task DisconnectAsync()
    {
        await StopLiveViewAsync();
        connected = false;
        CameraDisconnected?.Invoke();
    }
    public Task<CameraStatus> GetStatusAsync() => Task.FromResult(new CameraStatus(connected, live is not null, "mock", "Mock studio · sample graphics"));
    public Task StartLiveViewAsync()
    {
        if (!connected)
            throw new InvalidOperationException("Mock camera disconnected");
        if (live is not null)
            return Task.CompletedTask;
        live = new();
        var token = live.Token;
        loop = Task.Run(async () => { try { while (!token.IsCancellationRequested) { LiveViewFrameReceived?.Invoke(Render(800, 533, Interlocked.Increment(ref frame))); await Task.Delay(85, token); } } catch (OperationCanceledException) { } catch (Exception e) { CameraError?.Invoke(e.Message); } }, token);
        return Task.CompletedTask;
    }
    public async Task StopLiveViewAsync()
    {
        if (live is null)
            return;
        live.Cancel();
        if (loop is not null)
            await loop;
        live.Dispose();
        live = null;
    }
    public async Task<string> CaptureAsync(string destination, CancellationToken cancellationToken)
    {
        if (!connected)
            throw new InvalidOperationException("Camera disconnected");
        await Task.Delay(180, cancellationToken);
        Storage.Atomic(destination, Render(2400, 1600, Interlocked.Increment(ref frame)));
        PhotoCaptured?.Invoke(destination);
        return destination;
    }
    static byte[] Render(int w, int h, int frame)
    {
        using var b = new SKBitmap(w, h);
        using var c = new SKCanvas(b);
        c.Clear(new SKColor(24, 51, 76));
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), new[] { new SKColor(28, 70, 92), new SKColor(109, 81, 127) }, null, SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, w, h, p);
        p.Shader = null;
        for (var i = 0; i < 3; i++)
        {
            var x = w * (.25f + i * .25f);
            var y = h * .4f + (float)Math.Sin(frame * .07 + i) * h * .02f;
            p.Color = new SKColor((byte)(200 + i * 16), 190, 170);
            c.DrawCircle(x, y, w * .065f, p);
            p.Color = new SKColor((byte)(65 + i * 40), (byte)(130 + i * 20), 170);
            c.DrawRoundRect(x - w * .09f, y + w * .075f, w * .18f, h * .4f, 40, 40, p);
        }
        p.Color = SKColors.White;
        p.TextSize = w * .026f;
        c.DrawText("MOCK CAMERA / SAMPLE IMAGE", w * .04f, h * .92f, p);
        using var d = b.Encode(SKEncodedImageFormat.Jpeg, 92);
        return d.ToArray();
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
