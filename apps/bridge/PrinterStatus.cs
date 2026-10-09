using System.Runtime.InteropServices;
namespace Booth;
internal static class PrinterStatus
{
    public static bool Ready(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !OpenPrinter(name, out var handle, IntPtr.Zero)) return false;
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var size);
            if (size == 0) return false;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetPrinter(handle, 2, buffer, size, out _)) return false;
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                // Offline, paused, paper/error/door conditions. Driver-reported, not proof of paper delivery.
                const uint blocked = 0x1 | 0x2 | 0x8 | 0x10 | 0x40 | 0x80 | 0x800 | 0x40000 | 0x100000 | 0x400000;
                return (info.Status & blocked) == 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(handle); }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location, DevMode, SepFile, PrintProcessor, Datatype, Parameters, SecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePPM;
    }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetPrinter(IntPtr handle, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ClosePrinter(IntPtr handle);
}
