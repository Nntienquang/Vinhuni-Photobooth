using System.IO.Compression;
using System.Text.Json;
using SkiaSharp;

namespace Booth;
public sealed class Templates(Storage storage)
{
    public string Dir(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,80}$"))
            throw new ArgumentException("Invalid template id");
        return Path.Combine(storage.Root, "templates", id);
    }
    public List<Template> All() => Directory.GetFiles(Path.Combine(storage.Root, "templates"), "template.json", SearchOption.AllDirectories).Select(p => JsonSerializer.Deserialize<Template>(File.ReadAllText(p), Storage.Json)!).OrderBy(t => t.Order).ToList();
    public Template Get(string id) => JsonSerializer.Deserialize<Template>(File.ReadAllText(Path.Combine(Dir(id), "template.json")), Storage.Json)!;
    public string EnsureThumbnail(string id)
    {
        var path = Path.Combine(Dir(id), "thumbnail.jpg");
        if (!File.Exists(path))
        {
            using var bitmap = SKBitmap.Decode(Path.Combine(Dir(id), "frame.png")) ?? throw new InvalidDataException("Frame missing");
            using var thumbnail = bitmap.Resize(new SKImageInfo(300, Math.Max(1, (int)(300f * bitmap.Height / bitmap.Width))), SKFilterQuality.Medium) ?? throw new InvalidDataException("Frame invalid");
            using var encoded = thumbnail.Encode(SKEncodedImageFormat.Jpeg, 85);
            Storage.Atomic(path, encoded.ToArray());
        }
        return path;
    }
    public static IReadOnlyList<Slot> ExpandedSlots(Template t)
    {
        if (!t.DuplicateStrip)
            return t.Slots;
        var offset = t.DuplicateOffsetX ?? t.OutputWidth / 2f;
        // Mirror the layout within a half-sheet, then translate. Photo pixels stay readable.
        return [.. t.Slots, .. t.Slots.Select(s => s with { Id = s.Id + "-right",
            X = offset + (t.DuplicateMode == "mirror" ? t.OutputWidth / 2f - s.X - s.Width : s.X),
            Y = s.Y + t.DuplicateOffsetY, Rotation = t.DuplicateMode == "mirror" ? -s.Rotation : s.Rotation })];
    }
    public static void Validate(Template t)
    {
        if (t.Slots is null || t.Slots.Any(s => s is null)) throw new ArgumentException("Slots are required");
        var selectionCount = t.EffectiveSelectionCount;
        if (string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || t.OutputWidth is < 300 or > 6000 || t.OutputHeight is < 300 or > 6000 || (long)t.OutputWidth * t.OutputHeight > 24000000 || t.Dpi is < 72 or > 600 || t.ShotsRequired is < 1 or > 4 || selectionCount is < 1 or > 4 || t.EffectiveCaptureCount < selectionCount || t.Slots.Count < selectionCount || t.Slots.Count > 16 || t.Orientation is not ("portrait" or "landscape"))
            throw new ArgumentException("Invalid template dimensions or shot count");
        if (t.Slots.Select(s => s.Id).Distinct().Count() != t.Slots.Count)
            throw new ArgumentException("Duplicate slot id");
        if (t.DuplicateMode is not ("copy" or "mirror") || !float.IsFinite(t.DuplicateOffsetX ?? 0) || !float.IsFinite(t.DuplicateOffsetY) || t.EffectiveCaptureCount > 12)
            throw new ArgumentException("Invalid duplicate settings or capture count");
        foreach (var s in ExpandedSlots(t))
        {
            if (new[] { s.X, s.Y, s.Width, s.Height, s.Rotation, s.BorderRadius, s.FocalX, s.FocalY }.Any(v => !float.IsFinite(v)) || s.Width <= 0 || s.Height <= 0 || s.X < 0 || s.Y < 0 || s.X + s.Width > t.OutputWidth || s.Y + s.Height > t.OutputHeight || Math.Abs(s.Rotation) > 180 || s.BorderRadius < 0 || s.BorderRadius > Math.Min(s.Width, s.Height) / 2 || s.ShotIndex < 0 || s.ShotIndex >= selectionCount || s.FocalX is < 0 or > 1 || s.FocalY is < 0 or > 1)
                throw new ArgumentException("Slot outside canvas or invalid photo index");
        }
        if (Enumerable.Range(0, selectionCount).Any(i => !t.Slots.Any(s => s.ShotIndex == i)))
            throw new ArgumentException("Every shot must appear in a slot");
    }
    public void Save(Template t, byte[]? png = null)
    {
        Validate(t);
        var dir = Dir(t.Id);
        if (png is not null)
        {
            if (png.Length > 20 * 1024 * 1024 || png.Length < 8 || !png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new ArgumentException("Expected PNG up to 20 MB");
            using var data = SKData.CreateCopy(png);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                throw new ArgumentException("Invalid PNG");
            var uploadedWidth = codec.Info.Width;
            var uploadedHeight = codec.Info.Height;
            var expectedAspect = (double)t.OutputWidth / t.OutputHeight;
            var uploadedAspect = (double)uploadedWidth / uploadedHeight;
            if (Math.Abs(uploadedAspect - expectedAspect) > 0.005)
                throw new ArgumentException($"PNG dimensions {uploadedWidth}x{uploadedHeight} have the wrong aspect ratio; expected {t.OutputWidth}:{t.OutputHeight} ({expectedAspect:0.###}) and output size {t.OutputWidth}x{t.OutputHeight}");
            using var decoded = SKBitmap.Decode(png);
            if (decoded is null)
                throw new ArgumentException("Invalid PNG");
            using var normalized = decoded.Resize(new SKImageInfo(t.OutputWidth, t.OutputHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High) ?? throw new ArgumentException("Invalid PNG");
            Storage.Atomic(Path.Combine(dir, "frame.png"), png);
            using var thumbnail = normalized.Resize(new SKImageInfo(300, Math.Max(1, (int)(300f * normalized.Height / normalized.Width))), SKFilterQuality.Medium) ?? throw new ArgumentException("Invalid PNG");
            using var encoded = thumbnail.Encode(SKEncodedImageFormat.Jpeg, 85);
            Storage.Atomic(Path.Combine(dir, "thumbnail.jpg"), encoded.ToArray());
        }
        if (!File.Exists(Path.Combine(dir, "frame.png")))
            throw new ArgumentException("Frame PNG required");
        Storage.WriteJson(Path.Combine(dir, "template.json"), t with { OverlayPath = "frame.png", ThumbnailPath = "thumbnail.jpg" });
    }
    public void Delete(string id) => Directory.Delete(Dir(id), true);
    public Template Duplicate(string id)
    {
        var t = Get(id) with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Get(id).Name + " copy"
        };
        Save(t, File.ReadAllBytes(Path.Combine(Dir(id), "frame.png")));
        return t;
    }
    public byte[] Export(string id)
    {
        using var m = new MemoryStream();
        using (var zip = new ZipArchive(m, ZipArchiveMode.Create, true))
        {
            zip.CreateEntryFromFile(Path.Combine(Dir(id), "template.json"), "template.json");
            zip.CreateEntryFromFile(Path.Combine(Dir(id), "frame.png"), "frame.png");
            using var bmp = SKBitmap.Decode(Path.Combine(Dir(id), "frame.png"));
            using var thumb = bmp.Resize(new SKImageInfo(300, (int)(300f * bmp.Height / bmp.Width)), SKFilterQuality.Medium);
            using var encoded = thumb.Encode(SKEncodedImageFormat.Jpeg, 80);
            using var dest = zip.CreateEntry("thumbnail.jpg").Open();
            encoded.SaveTo(dest);
        }
        return m.ToArray();
    }
    public Template Import(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 3 || zip.Entries.Any(e => e.FullName is not ("template.json" or "frame.png" or "thumbnail.jpg") || e.Length > 20 * 1024 * 1024))
            throw new ArgumentException("Invalid template package");
        var metadata = zip.GetEntry("template.json") ?? throw new ArgumentException("Missing template.json");
        if (metadata.Length > 65536)
            throw new ArgumentException("Metadata too large");
        using var input = metadata.Open();
        var t = JsonSerializer.Deserialize<Template>(input, Storage.Json) ?? throw new ArgumentException("Invalid metadata");
        t = t with
        {
            Id = Guid.NewGuid().ToString("N")
        };
        using var png = (zip.GetEntry("frame.png") ?? throw new ArgumentException("Missing frame.png")).Open();
        using var m = new MemoryStream();
        png.CopyTo(m);
        Save(t, m.ToArray());
        return t;
    }
    public void Seed()
    {
        if (All().Count > 0)
            return;
        var templates = new[]{
            new Template("classic-4","Classic 4 Shot",1200,1800,300,4,"portrait",true,0,[new("1",90,180,450,320,ShotIndex:0),new("2",90,560,450,320,ShotIndex:1),new("3",90,940,450,320,ShotIndex:2),new("4",90,1320,450,320,ShotIndex:3)]) with { CaptureCount = 6, SelectionCount = 4, DuplicateStrip = true },
            new Template("double-2","Double Portrait",1200,1800,300,2,"portrait",true,1,[new("1",90,350,450,500,ShotIndex:0),new("2",90,950,450,500,ShotIndex:1)]) with { CaptureCount = 6, SelectionCount = 2, DuplicateStrip = true },
            new Template("classic-3","Classic 3 Shot",1200,1800,300,3,"portrait",true,2,[new("1",90,250,450,360,ShotIndex:0),new("2",90,720,450,360,ShotIndex:1),new("3",90,1190,450,360,ShotIndex:2)]) with { CaptureCount = 6, SelectionCount = 3, DuplicateStrip = true },
            new Template("hero-1","Single Hero",1800,1200,300,1,"landscape",true,3,[new("1",80,160,1640,850)]) with { CaptureCount = 6, SelectionCount = 1 }};
        foreach (var t in templates)
        {
            using var bitmap = new SKBitmap(t.OutputWidth, t.OutputHeight);
            using var c = new SKCanvas(bitmap);
            c.Clear(new SKColor(12, 28, 55));
            using var cut = new SKPaint { BlendMode = SKBlendMode.Clear };
            foreach (var s in ExpandedSlots(t))
                c.DrawRect(s.X, s.Y, s.Width, s.Height, cut);
            using var ink = new SKPaint { Color = new SKColor(104, 214, 235), TextSize = 44, IsAntialias = true };
            c.DrawText("VINHUNI / PHOTOBOOTH", 80, 100, ink);
            ink.TextSize = 30;
            c.DrawText("A MOMENT TO REMEMBER · 2026", 80, t.OutputHeight - 95, ink);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            Save(t, data.ToArray());
        }
    }
}
