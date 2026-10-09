using Booth;
using SkiaSharp;
using Xunit;
namespace Bridge.Tests;
public sealed class OrientationTests
{
    [Theory]
    [InlineData(1, "RGBY")][InlineData(2, "GRYB")][InlineData(3, "YBGR")][InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")][InlineData(6, "BRYG")][InlineData(7, "YGBR")][InlineData(8, "GYRB")]
    public void AllExifOrientationsPreservePixels(int orientation, string expected)
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        var colors = new Dictionary<char, SKColor> { ['R'] = SKColors.Red, ['G'] = SKColors.Lime, ['B'] = SKColors.Blue, ['Y'] = SKColors.Yellow };
        try
        {
            using var bitmap = new SKBitmap(80, 40); using var canvas = new SKCanvas(bitmap); using var paint = new SKPaint();
            foreach (var (key, index) in "RGBY".Select((key, index) => (key, index))) { paint.Color = colors[key]; canvas.DrawRect(index % 2 * 40, index / 2 * 20, 40, 20, paint); }
            using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100); var raw = jpeg.ToArray();
            byte[] app1 = [255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
            File.WriteAllBytes(file, raw.Take(2).Concat(app1).Concat(raw.Skip(2)).ToArray());
            using var result = PhotoComposer.Oriented(file); Assert.Equal(orientation >= 5 ? 40 : 80, result.Width);
            for (var i = 0; i < 4; i++) { var actual = result.GetPixel(result.Width / 4 + i % 2 * result.Width / 2, result.Height / 4 + i / 2 * result.Height / 2); var target = colors[expected[i]]; Assert.InRange(Math.Abs(actual.Red - target.Red), 0, 4); Assert.InRange(Math.Abs(actual.Green - target.Green), 0, 4); Assert.InRange(Math.Abs(actual.Blue - target.Blue), 0, 4); }
        }
        finally { File.Delete(file); }
    }
}
