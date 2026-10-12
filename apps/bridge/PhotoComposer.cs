using SkiaSharp;
namespace Booth;
public sealed class PhotoComposer
{
    public static SKRect Cover(float width, float height, float targetWidth, float targetHeight, float focalX = .5f, float focalY = .5f)
    {
        var scale = Math.Max(targetWidth / width, targetHeight / height);
        var w = targetWidth / scale;
        var h = targetHeight / scale;
var x = Math.Clamp(width * focalX - w / 2, 0, Math.Max(0, width - w));
var y = Math.Clamp(height * focalY - h / 2, 0, Math.Max(0, height - h));
        return new SKRect(x, y, x + w, y + h);
    }
    public static SKBitmap Oriented(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Invalid photo");
        using var raw = SKBitmap.Decode(codec);
        var origin = codec.EncodedOrigin;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? raw.Height : raw.Width, swap ? raw.Width : raw.Height);
        using var c = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                c.Translate(raw.Width, 0);
                c.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                c.Translate(raw.Width, raw.Height);
                c.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                c.Translate(0, raw.Height);
                c.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                c.RotateDegrees(90);
                c.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                c.Translate(raw.Height, 0);
                c.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                c.Translate(raw.Height, raw.Width);
                c.RotateDegrees(90);
                c.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                c.Translate(0, raw.Width);
                c.RotateDegrees(-90);
                break;
        }
        c.DrawBitmap(raw, 0, 0);
        return result;
    }
    public string Compose(Template t, IReadOnlyList<string> originals, string overlay, string directory, Settings settings)
    {
        Templates.Validate(t);
        if (originals.Count != t.EffectiveSelectionCount)
            throw new InvalidOperationException("Missing originals");
        using var bitmap = new SKBitmap(t.OutputWidth, t.OutputHeight);
        using var c = new SKCanvas(bitmap);
        c.Clear(SKColors.White);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        foreach (var slot in Templates.ExpandedSlots(t).OrderBy(s => s.Order))
        {
            using var photo = Oriented(originals[slot.ShotIndex]);
            c.Save();
            c.Translate(slot.X + slot.Width / 2, slot.Y + slot.Height / 2);
            c.RotateDegrees(slot.Rotation);
            var dest = new SKRect(-slot.Width / 2, -slot.Height / 2, slot.Width / 2, slot.Height / 2);
            using var clip = new SKRoundRect(dest, slot.BorderRadius, slot.BorderRadius);
            c.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            if (settings.MirrorFinal)
                c.Scale(-1, 1);
            c.DrawBitmap(photo, Cover(photo.Width, photo.Height, slot.Width, slot.Height, slot.FocalX, slot.FocalY), dest, paint);
            c.Restore();
        }
        using var frame = SKBitmap.Decode(overlay) ?? throw new InvalidDataException("Frame missing");
        c.DrawBitmap(frame, new SKRect(0, 0, t.OutputWidth, t.OutputHeight), paint);
        var path = Path.Combine(directory, settings.ExportPng ? "final.png" : "final.jpg");
        using var encoded = bitmap.Encode(settings.ExportPng ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, settings.JpegQuality);
        var bytes = encoded.ToArray();
        // Set JFIF density to the template DPI; output pixels remain unchanged.
        if (!settings.ExportPng && bytes.Length > 18 && bytes[6] == 74 && bytes[7] == 70 && bytes[8] == 73 && bytes[9] == 70)
        {
            bytes[13] = 1;
            bytes[14] = (byte)(t.Dpi >> 8);
            bytes[15] = (byte)t.Dpi;
            bytes[16] = (byte)(t.Dpi >> 8);
            bytes[17] = (byte)t.Dpi;
        }
        Storage.Atomic(path, bytes);
        return path;
    }
}

