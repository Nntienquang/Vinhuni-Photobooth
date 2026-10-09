using System.Drawing.Printing;
namespace Booth;
public interface IPrintService
{
    string Provider
    {
        get;
    }
    string[] Printers(); bool Ready(string printer); Task PrintAsync(string file, Settings settings);
}
public sealed class MockPrintService : IPrintService
{
    public string Provider => "mock"; public string[] Printers() => ["Mock photo printer"]; public bool Ready(string printer) => true;
    public async Task PrintAsync(string file, Settings settings)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("Final missing");
        await Task.Delay(500);
    }
}
public sealed class WindowsPrintService : IPrintService
{
    public string Provider => "windows";
    public string[] Printers() => PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
    public bool Ready(string printer) => PrinterStatus.Ready(printer);
    public Task PrintAsync(string file, Settings settings) => Task.Run(() =>
    {
        if (!Ready(settings.Printer))
            throw new InvalidOperationException("Select an installed Windows printer");
        using var image = System.Drawing.Image.FromFile(file);
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = settings.Printer;
        doc.PrinterSettings.Copies = (short)settings.Copies;
        doc.DocumentName = "VINHUNI PHOTOBOOTH " + Path.GetFileName(Path.GetDirectoryName(file));
        doc.PrintController = new StandardPrintController();
        // PrintPage graphics start at the hardware printable origin, not the paper edge.
        doc.OriginAtMargins = false;
        doc.DefaultPageSettings.PaperSize = new PaperSize("Photobooth", Math.Min(settings.PaperWidth, settings.PaperHeight), Math.Max(settings.PaperWidth, settings.PaperHeight));
        doc.DefaultPageSettings.Landscape = settings.PaperWidth > settings.PaperHeight;
        doc.DefaultPageSettings.Margins = new Margins(settings.Margin, settings.Margin, settings.Margin, settings.Margin);
        doc.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("Printer graphics unavailable");
            var printable = e.PageSettings.PrintableArea;
            // PrintableArea is reported in portrait coordinates, even for landscape pages.
            var printableSize = e.PageSettings.Landscape
                ? new System.Drawing.SizeF(printable.Height, printable.Width)
                : printable.Size;
            // Work entirely relative to the hardware origin. Do not subtract the rounded
            // HardMarginX/Y values or add the paper-relative PrintableArea.X/Y again.
            var bounds = new System.Drawing.RectangleF(settings.Margin, settings.Margin,
                printableSize.Width - 2 * settings.Margin, printableSize.Height - 2 * settings.Margin);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("Print margins leave no printable area");
            var destination = CenterImage(bounds, image.Size, settings.PrintScaling);
            var previousDestination = CenterImage(e.MarginBounds, image.Size, settings.PrintScaling);
            previousDestination.Offset(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY);
            Serilog.Log.ForContext<WindowsPrintService>().Information(
                "Print positioning (1/100 inch, destinations relative to hardware origin): Printer={Printer}, " +
                "PageBounds={PageBounds}, MarginBounds={MarginBounds}, HardMarginX={HardMarginX}, HardMarginY={HardMarginY}, " +
                "PrintableArea={PrintableArea}, Landscape={Landscape}, LandscapeAngle={LandscapeAngle}, PaperSize={PaperSize}, " +
                "PrintableBounds={PrintableBounds}, Scaling={Scaling}, ImageSize={ImageSize}, " +
                "PreviousDestination={PreviousDestination}, DestinationRectangle={DestinationRectangle}",
                doc.PrinterSettings.PrinterName, e.PageBounds, e.MarginBounds,
                e.PageSettings.HardMarginX, e.PageSettings.HardMarginY, printable,
                e.PageSettings.Landscape, doc.PrinterSettings.LandscapeAngle, e.PageSettings.PaperSize,
                bounds, settings.PrintScaling, image.Size, previousDestination, destination);
            var state = graphics.Save();
            try
            {
                graphics.SetClip(bounds, System.Drawing.Drawing2D.CombineMode.Intersect);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, destination);
            }
            finally
            {
                graphics.Restore(state);
            }
            e.HasMorePages = false;
        };
        doc.Print(); // Success means Windows spooler accepted the job, not physical delivery.
    });

    internal static System.Drawing.RectangleF CenterImage(System.Drawing.RectangleF bounds,
        System.Drawing.Size imageSize, string scaling)
    {
        var scale = scaling == "cover"
            ? Math.Max(bounds.Width / imageSize.Width, bounds.Height / imageSize.Height)
            : Math.Min(bounds.Width / imageSize.Width, bounds.Height / imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        return new System.Drawing.RectangleF(bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2, width, height);
    }
}
