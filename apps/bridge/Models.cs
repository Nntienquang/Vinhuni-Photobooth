namespace Booth;

public enum BoothState
{
    IDLE, FRAME_SELECTION, LIVE_VIEW, READY, COUNTDOWN, CAPTURING, BETWEEN_SHOTS, PHOTO_SELECTION, COMPOSING, FINAL_PREVIEW, PRINTING, QR_READY, THANK_YOU, MAINTENANCE, ERROR, ASSIGN, FILTER
}
public sealed record Slot(string Id, float X, float Y, float Width, float Height, float Rotation = 0, float BorderRadius = 0, int ShotIndex = 0, float FocalX = .5f, float FocalY = .5f, int Order = 0);
public sealed record Template(string Id, string Name, int OutputWidth, int OutputHeight, int Dpi, int ShotsRequired, string Orientation, bool Enabled, int Order, List<Slot> Slots)
{
    public string OverlayPath { get; init; } = "frame.png";
    public string ThumbnailPath { get; init; } = "thumbnail.jpg";
    public int CaptureCount { get; init; }
    public int SelectionCount { get; init; }
    public bool DuplicateStrip { get; init; }
    public string DuplicateMode { get; init; } = "copy";
    public float? DuplicateOffsetX { get; init; }
    public float DuplicateOffsetY { get; init; }
    public int EffectiveCaptureCount => CaptureCount > 0 ? CaptureCount : ShotsRequired;
    public int EffectiveSelectionCount => SelectionCount > 0 ? SelectionCount : ShotsRequired;
}
public sealed class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required Template Template
    {
        get; set;
    }
    public BoothState State { get; set; } = BoothState.LIVE_VIEW;
    public List<string> Shots { get; set; } = [];
    public List<int> SelectedShots { get; set; } = [];
    public int Retakes
    {
        get; set;
    }
    public bool Confirmed
    {
        get; set;
    }
    public string? FinalPath
    {
        get; set;
    }
    public string? ShareUrl
    {
        get; set;
    }
    public string PrintStatus { get; set; } = "none";
    public string UploadStatus { get; set; } = "none";
    public string? ShareId { get; set; }
    public string? ImageUrl { get; set; }
    public string? CloudLastError { get; set; }
    public DateTimeOffset? CloudUpdatedAt { get; set; }
    public void CopyCloudFrom(Session source)
    {
        UploadStatus = source.UploadStatus;
        ShareUrl = source.ShareUrl;
        ShareId = source.ShareId;
        ImageUrl = source.ImageUrl;
        CloudLastError = source.CloudLastError;
        CloudUpdatedAt = source.CloudUpdatedAt;
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string? Error
    {
        get; set;
    }
}
public sealed record Settings
{
    public int Countdown { get; init; } = 5;
    public double BetweenShots { get; init; } = 2;
    public bool AllowRetake { get; init; } = true;
    public int MaxRetake { get; init; } = 1;
    public bool MirrorPreview { get; init; } = true;
    public bool MirrorFinal
    {
        get; init;
    }
    public int LiveViewFps { get; init; } = 12;
    public bool AutoPrint
    {
        get; init;
    }
    public string Printer { get; init; } = "";
    public int Copies { get; init; } = 1;
    public int PaperWidth { get; init; } = 400;
    public int PaperHeight { get; init; } = 600;
    public int Margin
    {
        get; init;
    }
    public string PrintScaling { get; init; } = "contain";
    public bool CloudEnabled { get; init; } = true;
    public int JpegQuality { get; init; } = 94;
    public bool ExportPng
    {
        get; init;
    }
    public int ThankYouTimeout { get; init; } = 8;
    public int IdleTimeout { get; init; } = 60;
    public int PreviewTimeout { get; init; } = 120;
    public bool Sounds
    {
        get; init;
    }
    public bool Flash { get; init; } = true;
    public bool EventMode
    {
        get; init;
    }
    public string EventTitle { get; init; } = "VINHUNI CLUB'S DAY 2026";
    public string Accent { get; init; } = "#66d5ed";
    public string LiveViewFit { get; init; } = "contain";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EventTitle) || Accent is null || Printer is null || !double.IsFinite(BetweenShots)) throw new ArgumentException("Invalid settings");
        if (Countdown is not (3 or 5 or 10) || BetweenShots is < 0 or > 15 || MaxRetake is < 0 or > 5 || LiveViewFps is < 1 or > 15 || Copies is < 1 or > 10 || JpegQuality is < 70 or > 100 || ThankYouTimeout is < 3 or > 120 || IdleTimeout is < 15 or > 600 || PreviewTimeout is < 15 or > 600 || PaperWidth is < 100 or > 2000 || PaperHeight is < 100 or > 2000 || Margin < 0 || Margin * 2 >= Math.Min(PaperWidth, PaperHeight) || PrintScaling is not ("cover" or "contain") || LiveViewFit is not ("cover" or "contain") || EventTitle.Length > 150 || !System.Text.RegularExpressions.Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Invalid settings");
    }
}
public sealed record CameraDevice(string Id, string Provider, string Manufacturer, string Model, string ConnectionType);
public sealed record CameraStatus(bool Connected, bool LiveView, string Provider, string Model, string? Error = null,
    string Manufacturer = "", string ConnectionType = "", string? Availability = null);
public sealed record CameraPreferences(string Provider = "auto", bool AutoConnect = true, bool StartLiveView = true)
{
    public void Validate() { if (Provider is not ("auto" or "canon" or "sony" or "mock" or "webcam")) throw new ArgumentException("Invalid camera provider"); }
}
public sealed record Activity(DateTimeOffset Time, string Message);
public static class SessionRules
{
    public static void Require(Session s, params BoothState[] allowed)
    {
        if (!allowed.Contains(s.State))
            throw new InvalidOperationException($"Action unavailable in {s.State}");
    }
    public static void Retake(Session s, Settings settings)
    {
        Require(s, BoothState.FINAL_PREVIEW);
        if (!settings.AllowRetake || s.Retakes >= settings.MaxRetake || s.Confirmed)
            throw new InvalidOperationException("Retake limit reached");
    }
    public static void Print(Session s)
    {
        if (!s.Confirmed || s.FinalPath is null || s.PrintStatus is "sent" or "sending" or "uncertain")
            throw new InvalidOperationException("Print already submitted or not confirmed");
    }
}
