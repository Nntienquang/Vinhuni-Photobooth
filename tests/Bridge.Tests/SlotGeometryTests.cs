using Booth;
using SkiaSharp;
using Xunit;
namespace Bridge.Tests;

public sealed class SlotGeometryTests
{
    [Fact]
    public void CopyAndMirrorKeepSourcesAndMappingUnchanged()
    {
        var source = new Slot("source", 80, 180, 450, 320, Rotation: 15, FocalX: .2f);
        var template = new Template("t", "T", 1200, 1800, 300, 1, "portrait", true, 0, [source])
            { DuplicateStrip = true, DuplicateOffsetX = 610, DuplicateOffsetY = 20 };
        var copy = Templates.ExpandedSlots(template);
        Assert.Equal(source, copy[0]); Assert.Equal(690, copy[1].X); Assert.Equal(200, copy[1].Y);
        var mirror = Templates.ExpandedSlots(template with { DuplicateMode = "mirror" });
        Assert.Equal(source, mirror[0]); Assert.Equal(680, mirror[1].X); Assert.Equal(-15, mirror[1].Rotation);
        Assert.Equal(.2f, mirror[1].FocalX); Assert.Equal(0, mirror[1].ShotIndex);
        Templates.Validate(template);
        Assert.Throws<ArgumentException>(() => Templates.Validate(template with { DuplicateOffsetX = 1000 }));
    }

    [Fact]
    public void FinalUsesFocalCropRadiusRotationAndDuplicateCoordinates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slot-output-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            using var photo = new SKBitmap(600, 200);
            using (var canvas = new SKCanvas(photo)) { canvas.Clear(SKColors.Red); using var blue = new SKPaint { Color = SKColors.Blue }; canvas.DrawRect(300, 0, 300, 200, blue); }
            using var frame = new SKBitmap(800, 600); frame.Erase(SKColors.Transparent);
            var photoPath = Path.Combine(directory, "photo.png"); var framePath = Path.Combine(directory, "frame.png");
            using (var encoded = photo.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(photoPath, encoded.ToArray());
            using (var encoded = frame.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(framePath, encoded.ToArray());
            var template = new Template("t", "T", 800, 600, 300, 1, "landscape", true, 0,
                [new("slot", 60, 80, 100, 200, Rotation: 90, BorderRadius: 30, FocalX: 1)])
                { DuplicateStrip = true, DuplicateMode = "mirror", DuplicateOffsetX = 400, DuplicateOffsetY = 40 };
            var output = new PhotoComposer().Compose(template, [photoPath], framePath, directory, new Settings { ExportPng = true });
            using var result = SKBitmap.Decode(output);
            Assert.Equal(SKColors.Blue, result.GetPixel(110, 180)); // left center
            Assert.Equal(SKColors.Blue, result.GetPixel(690, 220)); // reflected center + offset
            Assert.Equal(SKColors.White, result.GetPixel(110, 90)); // rotated rectangle is only 100px high
            Assert.Equal(SKColors.White, result.GetPixel(12, 132)); // rounded corner outside clip
        }
        finally { Directory.Delete(directory, true); }
    }
}
