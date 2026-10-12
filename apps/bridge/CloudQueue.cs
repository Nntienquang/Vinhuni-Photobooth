using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Booth;

// Credentials stay in this service; the existing queue and session store are reused.
public sealed class CloudQueue(Storage storage, ILogger<CloudQueue> logger, HttpClient? httpClient = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly HttpClient client = httpClient ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    string BaseUrl => (Environment.GetEnvironmentVariable("CLOUD_SHARE_BASE_URL") ?? "https://vinhuni-photobooth-share.vercel.app").TrimEnd('/');
    public bool Configured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UPLOAD_API_KEY"));

    public async Task Upload(Session s, Func<Task>? changed = null)
    {
        await gate.WaitAsync();
        try
        {
            // Re-read under the queue lock so simultaneous retries cannot upload twice.
            var latest = storage.Find(s.Id);
            s.CopyCloudFrom(latest);
            if (s.UploadStatus == "uploaded") return;
            if (!latest.Confirmed || latest.FinalPath is null || !File.Exists(latest.FinalPath))
                throw new InvalidDataException();
            if (!Configured)
            {
                logger.LogWarning("Cloud sharing is disabled: bridge cloud configuration is missing or invalid");
                throw new InvalidOperationException();
            }
            using var file = File.OpenRead(latest.FinalPath);
            if (file.Length == 0 || file.Length > 20L * 1024 * 1024) throw new InvalidDataException();
            var signature = new byte[8];
            var count = await file.ReadAsync(signature);
            var mime = count >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff ? "image/jpeg"
                : count == 8 && signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
                : throw new InvalidDataException();
            file.Position = 0;
            Save("uploading");
            if (changed is not null) await changed();
            logger.LogInformation("Cloud upload started for session {Session}", s.Id);
            using var authorization = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/upload");
            authorization.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("UPLOAD_API_KEY"));
            authorization.Content = JsonContent.Create(new { contentType = mime, size = file.Length });
            using var response = await client.SendAsync(authorization);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var shareId = Required(root, "shareId");
            var shareUrl = Required(root, "shareUrl");
            var pathname = Required(root, "pathname");
            var token = Required(root, "clientToken");
            // Blob enforces the authorization service's token expiry on PUT.
            _ = root.GetProperty("expiresAt");
            if (!Uri.TryCreate(shareUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Authority != new Uri(BaseUrl).Authority || uri.UserInfo.Length != 0)
                throw new InvalidDataException();

            // Vercel Blob SDK put wire protocol (API v12). No image transforms.
            using var upload = new HttpRequestMessage(HttpMethod.Put, "https://vercel.com/api/blob/?pathname=" + Uri.EscapeDataString(pathname));
            upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            upload.Headers.Add("x-api-version", "12");
            upload.Headers.Add("x-vercel-blob-access", "public");
            upload.Headers.Add("x-content-type", mime);
            upload.Content = new StreamContent(file);
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            upload.Content.Headers.ContentLength = file.Length;
            using var uploaded = await client.SendAsync(upload);
            uploaded.EnsureSuccessStatusCode();
            using var blob = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync());
            var imageUrl = blob.RootElement.TryGetProperty("url", out var value) ? value.GetString() : null;
            if (imageUrl is not null && (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var image) || image.Scheme != "https" || !image.Host.EndsWith(".blob.vercel-storage.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException();
            s.ShareId = shareId;
            s.ShareUrl = shareUrl;
            s.ImageUrl = imageUrl;
            Save("uploaded");
            logger.LogInformation("Cloud upload completed for session {Session}", s.Id);
        }
        catch (Exception e)
        {
            // Never persist/log response bodies, exception messages, headers or credentials.
            var safe = e is HttpRequestException { StatusCode: { } status } ? $"HTTP {(int)status}"
                : e is OperationCanceledException ? "Upload timed out"
                : e is InvalidOperationException ? "Cloud sharing is not configured"
                : e is IOException ? "Final image unavailable or invalid"
                : "Cloud upload unavailable";
            Save("failed", safe);
            logger.LogWarning("Cloud upload failed for session {Session}: {Reason}", s.Id, safe);
        }
        finally { gate.Release(); }

        void Save(string status, string? error = null)
        {
            s.UploadStatus = status;
            s.CloudLastError = error;
            s.CloudUpdatedAt = DateTimeOffset.UtcNow;
            storage.UpdateUpload(s.Id, status, s.ShareUrl, s.ShareId, s.ImageUrl, error, s.CloudUpdatedAt);
        }
    }
    static string Required(JsonElement json, string name) => json.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException();
}
