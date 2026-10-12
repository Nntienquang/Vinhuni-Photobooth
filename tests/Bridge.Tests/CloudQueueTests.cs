using System.Net;
using System.Text.Json;
using Booth;
using Booth.Camera;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bridge.Tests;

public sealed class CloudQueueTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "booth-cloud-" + Guid.NewGuid());
    readonly string? oldRoot = Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
    readonly string? oldKey = Environment.GetEnvironmentVariable("UPLOAD_API_KEY");
    readonly string? oldUrl = Environment.GetEnvironmentVariable("CLOUD_SHARE_BASE_URL");
    readonly Storage storage;
    readonly Session session;
    readonly byte[] bytes = [255, 216, 255, 224, 1, 2, 3, 4, 5];
    const string Base = "https://share.example.test";
    public CloudQueueTests()
    {
        Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR", root);
        Environment.SetEnvironmentVariable("UPLOAD_API_KEY", "test-api-secret");
        Environment.SetEnvironmentVariable("CLOUD_SHARE_BASE_URL", Base);
        storage = new Storage();
        session = new Session { Template = new("test", "Test", 600, 900, 300, 1, "portrait", true, 0, []), Confirmed = true, FinalPath = Path.Combine(root, "final.jpg"), State = BoothState.QR_READY };
        File.WriteAllBytes(session.FinalPath, bytes);
        storage.Save(session);
    }
    static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    static HttpResponseMessage Authorization() => Json(new { shareId = "share-id", shareUrl = Base + "/p/share-id", pathname = "photos/with space.jpg", clientToken = "test-client-secret", expiresAt = 2000000000000L });
    CloudQueue Queue(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(storage, NullLogger<CloudQueue>.Instance, new HttpClient(new Handler(respond)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoStepUploadPreservesBytesAndPrintStateAndNeverPersistsSecrets(bool png)
    {
        var original = png ? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 } : bytes;
        File.WriteAllBytes(session.FinalPath!, original);
        var mime = png ? "image/png" : "image/jpeg";
        var calls = 0;
        var queue = Queue(async request =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(Base + "/api/upload", request.RequestUri!.AbsoluteUri);
                Assert.Equal("test-api-secret", request.Headers.Authorization!.Parameter);
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                Assert.Equal(2, body.RootElement.EnumerateObject().Count());
                Assert.Equal(mime, body.RootElement.GetProperty("contentType").GetString());
                Assert.Equal(original.Length, body.RootElement.GetProperty("size").GetInt64());
                return Authorization();
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("https://vercel.com/api/blob/?pathname=photos%2Fwith%20space.jpg", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-client-secret", request.Headers.Authorization!.Parameter);
            Assert.Equal("12", request.Headers.GetValues("x-api-version").Single());
            Assert.Equal(mime, request.Headers.GetValues("x-content-type").Single());
            Assert.Equal(original.Length, request.Content!.Headers.ContentLength);
            Assert.Equal(original, await request.Content.ReadAsByteArrayAsync());
            return Json(new { url = "https://store.public.blob.vercel-storage.com/photo.jpg" });
        });
        var stale = storage.Find(session.Id);
        await queue.Upload(session, () =>
        {
            Assert.Equal("uploading", storage.Find(session.Id).UploadStatus);
            // A simultaneous print save must preserve upload progress.
            stale.PrintStatus = "sent";
            storage.Save(stale);
            Assert.Equal("uploading", storage.Find(session.Id).UploadStatus);
            return Task.CompletedTask;
        });
        Assert.Equal(2, calls);
        var saved = new Storage().Find(session.Id);
        Assert.Equal("uploaded", saved.UploadStatus);
        Assert.Equal("share-id", saved.ShareId);
        Assert.Equal(Base + "/p/share-id", saved.ShareUrl);
        Assert.NotNull(saved.ImageUrl);
        Assert.NotNull(saved.CloudUpdatedAt);
        Assert.Null(saved.CloudLastError);
        Assert.Equal("sent", saved.PrintStatus);
        Assert.Equal(BoothState.QR_READY, saved.State);
        Assert.Equal(original, File.ReadAllBytes(session.FinalPath!));
        storage.Save(stale);
        Assert.Equal(saved.ShareId, storage.Find(session.Id).ShareId);
        var json = File.ReadAllText(Path.Combine(storage.SessionDir(session), "session.json"));
        Assert.DoesNotContain("test-api-secret", json);
        Assert.DoesNotContain("test-client-secret", json);
        Assert.DoesNotContain("clientToken", json);
        await queue.Upload(stale);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureIsSafeAndRetryUploadsExistingFinal(bool failBlob)
    {
        var fail = true;
        var queue = Queue(request =>
        {
            if (fail && (failBlob || request.Method == HttpMethod.Post))
            {
                if (!failBlob || request.Method == HttpMethod.Put)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("test-api-secret test-client-secret") });
            }
            return Task.FromResult(request.Method == HttpMethod.Post ? Authorization() : Json(new { url = "https://store.public.blob.vercel-storage.com/photo.jpg" }));
        });
        session.PrintStatus = "sent";
        storage.Save(session);
        await queue.Upload(session);
        Assert.Equal("failed", session.UploadStatus);
        Assert.Equal("HTTP 503", session.CloudLastError);
        Assert.Null(session.ShareUrl);
        Assert.Null(session.Error);
        Assert.Equal("sent", storage.Find(session.Id).PrintStatus);
        fail = false;
        await queue.Upload(session);
        Assert.Equal("uploaded", session.UploadStatus);
        Assert.Null(session.CloudLastError);
        Assert.Equal(bytes, File.ReadAllBytes(session.FinalPath!));
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("missing-file")]
    [InlineData("oversized")]
    [InlineData("invalid-image")]
    [InlineData("unconfirmed")]
    public async Task InvalidInputsNeverSendNetworkRequests(string scenario)
    {
        if (scenario == "missing-key") Environment.SetEnvironmentVariable("UPLOAD_API_KEY", null);
        if (scenario == "missing-file") File.Delete(session.FinalPath!);
        if (scenario == "oversized") { using var file = File.OpenWrite(session.FinalPath!); file.SetLength(20L * 1024 * 1024 + 1); }
        if (scenario == "invalid-image") File.WriteAllText(session.FinalPath!, "invalid");
        if (scenario == "unconfirmed") { session.Confirmed = false; storage.Save(session); }
        var calls = 0;
        await Queue(_ => { calls++; return Task.FromResult(Authorization()); }).Upload(session);
        Assert.Equal(0, calls);
        Assert.Equal("failed", session.UploadStatus);
        Assert.Null(session.Error);
    }

    [Fact]
    public async Task OfflineExceptionDoesNotLeakDetailsAndCanRetry()
    {
        await Queue(_ => throw new HttpRequestException("test-api-secret test-client-secret")).Upload(session);
        Assert.Equal("failed", session.UploadStatus);
        Assert.Equal("Cloud upload unavailable", session.CloudLastError);
        Assert.Null(session.Error);
        Assert.True(File.Exists(session.FinalPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SixCaptureFourAssignmentFlowPrintsAndFinishesWhileUploadIsBlockedThenRetries(bool autoPrint)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = true;
        var calls = 0;
        var queue = Queue(async request =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (fail) throw new HttpRequestException("offline");
            return request.Method == HttpMethod.Post ? Authorization() : Json(new { url = "https://store.public.blob.vercel-storage.com/photo.jpg" });
        });
        var templates = new Templates(storage);
        templates.Seed();
        await using var camera = new MockCameraProvider();
        await camera.InitializeAsync();
        await camera.ConnectAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        using var provider = services.BuildServiceProvider();
        using var engine = new BoothEngine(storage, templates, new PhotoComposer(), camera, new MockPrintService(), queue,
            provider.GetRequiredService<IHubContext<BoothHub>>(), NullLogger<BoothEngine>.Instance);
        await engine.Configure(new Settings { Countdown = 3, BetweenShots = 0, AutoPrint = autoPrint });
        await engine.Select();
        await engine.Start("classic-4");
        var active = engine.Current!;
        await engine.Capture(active.Id);
        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (engine.State != BoothState.ASSIGN && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Equal(BoothState.ASSIGN, engine.State);
        Assert.Equal(6, active.Shots.Count);
        Assert.Equal(0, calls);
        await engine.AssignPhotos(active.Id, [0, 1, 3, 5]);
        await engine.Compose(active.Id);
        Assert.Equal(BoothState.FINAL_PREVIEW, engine.State);
        Assert.True(File.Exists(active.FinalPath));
        var original = File.ReadAllBytes(active.FinalPath!);
        Assert.Equal(0, calls);
        try
        {
            await engine.Confirm(active.Id).WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!autoPrint) await engine.Print(active.Id).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("sent", active.PrintStatus);
            await engine.Finish().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(BoothState.THANK_YOU, engine.State);
            Assert.Null(active.Error);
            await engine.Reset().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(BoothState.IDLE, engine.State);
        }
        finally { release.TrySetResult(); }
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (storage.Find(active.Id).UploadStatus != "failed" && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal("failed", storage.Find(active.Id).UploadStatus);
        fail = false;
        await engine.RetryUpload(active.Id);
        var saved = storage.Find(active.Id);
        Assert.Equal("uploaded", saved.UploadStatus);
        Assert.Equal("sent", saved.PrintStatus);
        Assert.Equal(BoothState.THANK_YOU, saved.State);
        Assert.Equal(new[] { 0, 1, 3, 5 }, saved.SelectedShots);
        Assert.Equal(6, saved.Shots.Count);
        Assert.Null(saved.Error);
        Assert.Equal(original, File.ReadAllBytes(saved.FinalPath!));
        Assert.Null(engine.Current);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR", oldRoot);
        Environment.SetEnvironmentVariable("UPLOAD_API_KEY", oldKey);
        Environment.SetEnvironmentVariable("CLOUD_SHARE_BASE_URL", oldUrl);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
