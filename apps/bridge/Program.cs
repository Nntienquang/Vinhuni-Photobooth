using System.Text.Json;
using System.Text.Json.Serialization;
using Booth;
using Booth.Camera;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:4310");
builder.Host.UseSerilog((context, config) => config
    .Filter.ByExcluding(logEvent => logEvent.Level == LogEventLevel.Information
        && logEvent.Properties.TryGetValue("RequestPath", out var requestPath)
        && requestPath.ToString().Trim('"') == "/api/camera/webcam/frame")
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(), "logs/booth-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 22 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 22 * 1024 * 1024);
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<Storage>();
builder.Services.AddSingleton<Templates>();
builder.Services.AddSingleton<PhotoComposer>();
builder.Services.AddSingleton<CloudQueue>();
builder.Services.AddSingleton<RuntimeCameraProvider>();
builder.Services.AddSingleton<ICameraProvider>(s => s.GetRequiredService<RuntimeCameraProvider>());
builder.Services.AddSingleton<IPrintService>(_ => Environment.GetEnvironmentVariable("PRINT_PROVIDER") == "windows" ? new WindowsPrintService() : new MockPrintService());
builder.Services.AddSingleton<BoothEngine>();
builder.Services.AddHostedService(s => s.GetRequiredService<BoothEngine>());
var app = builder.Build();
app.Services.GetRequiredService<Templates>().Seed();
app.Use(async (ctx, next) =>
{
    // Loopback binding alone does not protect a local device from hostile websites / DNS rebinding.
    var host = ctx.Request.Host.Host;
    var origin = ctx.Request.Headers.Origin.ToString();
    bool allowedOrigin = string.IsNullOrEmpty(origin) || origin == "http://127.0.0.1:4310" || origin == "http://127.0.0.1:5173" || origin == "http://localhost:5173";
    if (host is not ("127.0.0.1" or "localhost") || !allowedOrigin)
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS") && ctx.Request.Headers["X-Booth-Client"] != "1")
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/media"))
        ctx.Response.Headers.CacheControl = "no-store";
    try
    {
        await next();
    }
    catch (Exception e) { app.Logger.LogError(e, "Request failed {Path}", ctx.Request.Path); if (ctx.Response.HasStarted) return; ctx.Response.StatusCode = e is ArgumentException or InvalidDataException ? 400 : e is InvalidOperationException ? 409 : e is KeyNotFoundException or FileNotFoundException ? 404 : 500; await ctx.Response.WriteAsJsonAsync(new { error = ctx.Response.StatusCode == 500 ? "Operation failed. Check admin logs." : e.Message }); }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<BoothHub>("/hubs/booth");
app.MapGet("/api/system/status", (BoothEngine e) => e.Status());
app.MapGet("/api/settings", (BoothEngine e) => e.Settings);
app.MapPut("/api/settings", async (Settings s, BoothEngine e) => { await e.Configure(s); return Results.Ok(s); });
app.MapPost("/api/booth/start", async (BoothEngine e) => { await e.Reset(); return Results.Ok(); });
app.MapPost("/api/booth/pause", async (BoothEngine e) => { await e.Pause(); return Results.Ok(); });
app.MapPost("/api/booth/select", async (BoothEngine e) => { await e.Select(); return Results.Ok(); });
app.MapPost("/api/booth/finish", async (BoothEngine e) => { await e.Finish(); return Results.Ok(); });
app.MapGet("/api/camera/status", (ICameraProvider c) => c.GetStatusAsync());
app.MapGet("/api/camera/preferences", (RuntimeCameraProvider c) => c.Preferences);
app.MapPut("/api/camera/preferences", async (CameraPreferences preferences, RuntimeCameraProvider c, BoothEngine e) => { await e.CameraAction(() => c.SavePreferencesAsync(preferences)); return Results.Ok(c.Preferences); });
app.MapPost("/api/camera/provider", async (CameraPreferences preferences, RuntimeCameraProvider c, BoothEngine e) => { await e.CameraAction(() => c.SwitchAsync(preferences)); return Results.Ok(await c.GetStatusAsync()); });
app.MapPost("/api/camera/webcam/frame", async (HttpRequest request, RuntimeCameraProvider c) => { using var input = new MemoryStream(); await request.Body.CopyToAsync(input); await c.SubmitWebcamFrameAsync(input.ToArray()); return Results.Ok(); });
app.MapPost("/api/camera/scan", async (RuntimeCameraProvider c, BoothEngine e) => { IReadOnlyList<CameraDevice> devices = []; await e.CameraAction(async () => devices = await c.ScanAsync()); return Results.Ok(devices); });
app.MapPost("/api/camera/connect", async (ICameraProvider c, BoothEngine e) => { await e.RecoverCamera(c.ConnectAsync); return Results.Ok(await c.GetStatusAsync()); });
app.MapPost("/api/camera/reconnect", async (RuntimeCameraProvider c, BoothEngine e) => { await e.RecoverCamera(c.ReconnectAsync); return Results.Ok(await c.GetStatusAsync()); });
app.MapPost("/api/camera/disconnect", async (BoothEngine e) => { await e.DisconnectCamera(); return Results.Ok(); });
app.MapPost("/api/camera/live-view/start", async (ICameraProvider c, BoothEngine e) => { await e.CameraAction(c.StartLiveViewAsync); return Results.Ok(); });
app.MapPost("/api/camera/live-view/stop", async (ICameraProvider c, BoothEngine e) => { await e.CameraAction(c.StopLiveViewAsync); return Results.Ok(); });
app.MapPost("/api/camera/capture", async (ICameraProvider c, Storage s, BoothEngine e) => { string? path = null; await e.CameraAction(async () => { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); path = await c.CaptureAsync(Path.Combine(s.Root, "originals", "test_" + Guid.NewGuid().ToString("N") + ".jpg"), timeout.Token); e.Log("Test capture saved: " + path); }); return Results.Ok(new { localJpegPath = path }); });
app.MapGet("/api/templates", (Templates t) => t.All());
app.MapPost("/api/templates", async (HttpRequest r, Templates t) => { var form = await r.ReadFormAsync(); var metadata = JsonSerializer.Deserialize<Template>(form["metadata"].ToString(), Storage.Json) ?? throw new ArgumentException("Missing metadata"); var file = form.Files.GetFile("file") ?? throw new ArgumentException("Missing PNG"); if (!file.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.ContentType is not ("image/png" or "application/octet-stream")) throw new ArgumentException("Expected a PNG frame"); using var m = new MemoryStream(); await file.CopyToAsync(m); t.Save(metadata, m.ToArray()); return Results.Ok(metadata); });
app.MapPut("/api/templates/{id}", (string id, Template metadata, Templates t) => { if (id != metadata.Id) throw new ArgumentException("ID mismatch"); var old = t.Get(id); if (old.OutputWidth != metadata.OutputWidth || old.OutputHeight != metadata.OutputHeight) throw new ArgumentException("Upload a new PNG to change output size"); t.Save(metadata); return Results.Ok(metadata); });
app.MapDelete("/api/templates/{id}", (string id, Templates t) => { t.Delete(id); return Results.Ok(); });
app.MapPost("/api/templates/{id}/duplicate", (string id, Templates t) => t.Duplicate(id));
app.MapGet("/api/templates/{id}/export", (string id, Templates t) => Results.File(t.Export(id), "application/zip", id + ".zip"));
app.MapPost("/api/templates/import", async (HttpRequest r, Templates t) => { var form = await r.ReadFormAsync(); using var input = (form.Files.GetFile("file") ?? throw new ArgumentException("Missing ZIP")).OpenReadStream(); return Results.Ok(t.Import(input)); });
app.MapGet("/media/templates/{id}", (string id, Templates t) => Results.File(Path.Combine(t.Dir(id), "frame.png"), "image/png"));
app.MapGet("/media/templates/{id}/thumbnail", (string id, Templates t) => Results.File(t.EnsureThumbnail(id), "image/jpeg"));
app.MapPost("/api/session", async (StartSession request, BoothEngine e) => { await e.Start(request.TemplateId); return Results.Ok(e.Current); });
app.MapGet("/api/session/current", (BoothEngine e) => e.Current);
app.MapGet("/api/sessions", (Storage s) => s.Sessions());
app.MapPost("/api/session/{id}/capture", async (string id, BoothEngine e) => { await e.Capture(id); return Results.Accepted(); });
app.MapPost("/api/session/{id}/assign", async (string id, PhotoSelection request, BoothEngine e) => { await e.AssignPhotos(id, request.Indices); return Results.Ok(); });
app.MapPost("/api/session/{id}/select", async (string id, PhotoSelection request, BoothEngine e) => { await e.SelectPhotos(id, request.Indices); return Results.Ok(); });
app.MapPost("/api/session/{id}/reorder", async (string id, PhotoSelection request, BoothEngine e) => { await e.ReorderPhotos(id, request.Indices); return Results.Ok(); });
app.MapPost("/api/session/{id}/compose", async (string id, BoothEngine e) => { await e.Compose(id); return Results.Ok(); });
app.MapPost("/api/session/{id}/retake", async (string id, BoothEngine e) => { await e.Retake(id); return Results.Ok(); });
app.MapPost("/api/session/{id}/confirm", async (string id, BoothEngine e) => { await e.Confirm(id); return Results.Ok(); });
app.MapPost("/api/print/{id}", async (string id, BoothEngine e) => { if (e.Current?.Id == id) await e.Print(id); else await e.Reprint(id); return Results.Ok(); });
app.MapGet("/api/prints", (Storage s) => s.PrintHistory());
app.MapPost("/api/print/{id}/resolve", async (string id, PrintResolution resolution, BoothEngine e) => { await e.ResolvePrint(id, resolution.AlreadyPrinted); return Results.Ok(); });
app.MapGet("/api/printers", (IPrintService p) => p.Printers());
app.MapPost("/api/printer/test", async (IPrintService p, Storage s, BoothEngine e, PhotoComposer composer, Templates templates) => { await e.Exclusive(async () => { var t = templates.All().First(); using var mock = new MockDisposable(); var files = new List<string>(); await mock.Camera.ConnectAsync(); for (var i = 0; i < t.ShotsRequired; i++) files.Add(await mock.Camera.CaptureAsync(Path.Combine(s.Root, "originals", $"test-print-{i}.jpg"), CancellationToken.None)); var final = composer.Compose(t, files, Path.Combine(templates.Dir(t.Id), "frame.png"), Path.Combine(s.Root, "finals", "test"), e.Settings); await p.PrintAsync(final, e.Settings); e.Log("Test print submitted"); }); return Results.Ok(); });
app.MapPost("/api/cloud/retry", async (BoothEngine e) => { await e.RetryUploads(); return Results.Ok(); });
app.MapPost("/api/cloud/{id}/upload", async (string id, BoothEngine e) => { await e.RetryUpload(id); return Results.Ok(); });
app.MapGet("/media/sessions/{id}/final", (string id, Storage s) => { var session = s.Find(id); return session.FinalPath is null ? Results.NotFound() : Results.File(session.FinalPath, session.FinalPath.EndsWith(".png") ? "image/png" : "image/jpeg"); });
app.MapGet("/media/sessions/{id}/shot/{index:int}", (string id, int index, Storage s) => { var session = s.Find(id); return index < 0 || index >= session.Shots.Count ? Results.NotFound() : Results.File(session.Shots[index], "image/jpeg"); });
app.MapPost("/api/storage/open", (Storage s) => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", s.Root) { UseShellExecute = true }); return Results.Ok(); });
app.MapFallbackToFile("index.html");
app.Run();
record StartSession(string TemplateId);
record PrintResolution(bool AlreadyPrinted);
record PhotoSelection(int[] Indices);
sealed class MockDisposable : IDisposable
{
    public MockCameraProvider Camera { get; } = new(); public void Dispose() => Camera.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
