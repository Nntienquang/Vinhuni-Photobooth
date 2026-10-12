using System.Net.Http.Headers;
using System.Text.Json;
namespace Booth;
public sealed class CloudQueue(Storage storage, ILogger<CloudQueue> logger)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public bool Configured => Uri.TryCreate(Environment.GetEnvironmentVariable("PHOTOBOOTH_CLOUD_URL"), UriKind.Absolute, out var uri) && uri.Scheme == "https" && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PHOTOBOOTH_UPLOAD_TOKEN"));
    public async Task Upload(Session s)
    {
        if (!s.Confirmed || s.FinalPath is null)
            throw new InvalidOperationException("Only confirmed finals can upload");
        if (!Configured)
        {
            s.UploadStatus = "pending";
            storage.UpdateUpload(s.Id, s.UploadStatus, s.ShareUrl);
            return;
        }
        await gate.WaitAsync();
        try
        {
            if (s.UploadStatus == "uploaded")
                return;
            s.UploadStatus = "pending";
            storage.UpdateUpload(s.Id, s.UploadStatus, s.ShareUrl);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("PHOTOBOOTH_UPLOAD_TOKEN"));
            using var body = new MultipartFormDataContent();
            using var file = File.OpenRead(s.FinalPath);
            using var content = new StreamContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(s.FinalPath.EndsWith(".png") ? "image/png" : "image/jpeg");
            body.Add(content, "file", Path.GetFileName(s.FinalPath));
            body.Add(new StringContent(Guid.ParseExact(s.Id, "N").ToString()), "shareId");
            var baseUrl = Environment.GetEnvironmentVariable("PHOTOBOOTH_CLOUD_URL")!.TrimEnd('/');
            using var response = await client.PostAsync(baseUrl + "/api/upload", body);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var url = json.RootElement.GetProperty("shareUrl").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != new Uri(baseUrl).Host)
                throw new InvalidDataException("Invalid share URL");
            s.ShareUrl = url;
            s.UploadStatus = "uploaded";
            storage.UpdateUpload(s.Id, s.UploadStatus, s.ShareUrl);
        }
        catch (Exception e) { s.UploadStatus = "pending"; storage.UpdateUpload(s.Id, s.UploadStatus, s.ShareUrl); logger.LogWarning("Cloud upload pending for {Session}: {Type}", s.Id, e.GetType().Name); }
        finally { gate.Release(); }
    }
}
