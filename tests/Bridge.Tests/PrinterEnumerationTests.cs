using Booth;
using System.Drawing.Printing;
using Xunit;

namespace Bridge.Tests;
public sealed class PrinterEnumerationTests
{
    [Fact]
    public void WindowsProviderEnumeratesInstalledPrintersWithoutPrinting()
    {
        var expected = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
        Assert.Equal(expected, new WindowsPrintService().Printers());
    }
}
