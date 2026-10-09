using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace Booth.Camera;

/// <summary>All EDSDK calls and callbacks are confined to one STA worker with EdsGetEvent pumping.</summary>
public sealed class CanonEdSdkService : IAsyncDisposable
{
    readonly BlockingCollection<Action> commands = new(); readonly Thread worker; readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    IntPtr camera; bool initialized, live, stop; string? error; string model = "No camera detected"; TaskCompletionSource<string>? capture; string? destination; DateTime nextFrame;
    readonly Native.ObjectHandler objectHandler; readonly Native.StateHandler stateHandler;
    public event Action<byte[]>? Frame; public event Action<string>? Photo; public event Action? Connected; public event Action? Disconnected; public event Action<string>? Error;
    public CanonEdSdkService()
    {
        objectHandler = ObjectEvent;
        stateHandler = StateEvent;
        worker = new Thread(Run) { Name = "Canon EDSDK STA", IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    void Run()
    {
        started.SetResult();
        while (!stop)
        {
            try
            {
                if (commands.TryTake(out var action, 10))
                    action();
                if (initialized)
                {
                    Check(Native.EdsGetEvent());
                    if (live && camera != IntPtr.Zero && DateTime.UtcNow >= nextFrame)
                    {
                        nextFrame = DateTime.UtcNow.AddMilliseconds(83);
                        ReadEvf();
                    }
                }
            }
            catch (Exception e) { error = e.Message; if (camera != IntPtr.Zero) DisconnectCore(); Error?.Invoke(e.Message); }
        }
    }
    Task Invoke(Action action)
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Add(() => { try { action(); t.SetResult(); } catch (Exception e) { error = e.Message; Error?.Invoke(e.Message); t.SetException(e); } });
        return t.Task;
    }
    static void Check(uint code)
    {
        if (code != 0)
            throw new InvalidOperationException($"Canon EDSDK error 0x{code:X8}");
    }
    public async Task Initialize()
    {
        await started.Task;
        await Invoke(() => { if (initialized) return; try { Check(Native.EdsInitializeSDK()); initialized = true; } catch (DllNotFoundException) { throw new InvalidOperationException("Canon SDK missing. Install licensed Windows x64 EDSDK DLLs in vendor/canon and rebuild; or use CAMERA_PROVIDER=mock."); } });
    }
    public Task Connect() => Invoke(() =>
    {
        if (camera != IntPtr.Zero)
            return;
        if (!initialized)
            throw new InvalidOperationException("Initialize SDK first");
        Check(Native.EdsGetCameraList(out var list));
        try
        {
            Check(Native.EdsGetChildCount(list, out var count));
            for (int i = 0; i < count; i++)
            {
                Check(Native.EdsGetChildAtIndex(list, i, out var candidate));
                Check(Native.EdsGetDeviceInfo(candidate, out var info));
                if (IsSupportedModel(info.Description))
                {
                    camera = candidate;
                    model = info.Description;
                    break;
                }
                Native.EdsRelease(candidate);
            }
            if (camera == IntPtr.Zero)
                throw new InvalidOperationException("EOS R50 V / EOS RP not found. Check USB data cable, still-photo mode, and close EOS Utility.");
            try
            {
                Check(Native.EdsOpenSession(camera));
                Check(Native.EdsSetObjectEventHandler(camera, 0x200, objectHandler, IntPtr.Zero));
                Check(Native.EdsSetCameraStateEventHandler(camera, 0x300, stateHandler, IntPtr.Zero));
                uint host = 2;
                Check(Native.EdsSetPropertyData(camera, 0xB, 0, 4, ref host));
                var capacity = new Native.Capacity { NumberOfFreeClusters = int.MaxValue, BytesPerSector = 4096, Reset = 1 };
                Check(Native.EdsSetCapacity(camera, capacity));
                error = null;
                Connected?.Invoke();
            }
            catch { Native.EdsCloseSession(camera); Native.EdsRelease(camera); camera = IntPtr.Zero; throw; }
        }
        finally { Native.EdsRelease(list); }
    });
    public static bool IsSupportedModel(string model)
    {
        var normalized = model.Replace(" ", "").ToUpperInvariant();
        return normalized is "CANONEOSR50V" or "EOSR50V" or "CANONEOSRP" or "EOSRP";
    }
    public async Task<IReadOnlyList<CameraDevice>> Scan()
    {
        var devices = new List<CameraDevice>();
        await Initialize();
        await Invoke(() =>
        {
            Check(Native.EdsGetCameraList(out var list));
            try
            {
                Check(Native.EdsGetChildCount(list, out var count));
                for (var i = 0; i < count; i++)
                {
                    Check(Native.EdsGetChildAtIndex(list, i, out var candidate));
                    try { Check(Native.EdsGetDeviceInfo(candidate, out var info)); if (IsSupportedModel(info.Description)) devices.Add(new(info.Port, "canon", "Canon", info.Description, "USB")); }
                    finally { Native.EdsRelease(candidate); }
                }
            }
            finally { Native.EdsRelease(list); }
        });
        return devices;
    }
    public Task StartLive() => Invoke(() => { if (camera == IntPtr.Zero) throw new InvalidOperationException("Camera disconnected"); uint mode = 1; Check(Native.EdsSetPropertyData(camera, 0x501, 0, 4, ref mode)); Check(Native.EdsGetPropertyData(camera, 0x500, 0, 4, out var output)); output |= 2; Check(Native.EdsSetPropertyData(camera, 0x500, 0, 4, ref output)); live = true; });
    public Task StopLive() => Invoke(StopLiveCore);
    void StopLiveCore()
    {
        if (camera != IntPtr.Zero && live)
        {
            if (Native.EdsGetPropertyData(camera, 0x500, 0, 4, out var output) == 0)
            {
                output &= ~2u;
                Native.EdsSetPropertyData(camera, 0x500, 0, 4, ref output);
            }
        }
        live = false;
    }
    void ReadEvf()
    {
        IntPtr stream = IntPtr.Zero, image = IntPtr.Zero;
        try
        {
            Check(Native.EdsCreateMemoryStream(0, out stream));
            Check(Native.EdsCreateEvfImageRef(stream, out image));
            var result = Native.EdsDownloadEvfImage(camera, image);
            if (result is 0xA102 or 0x81)
                return;
            Check(result);
            Check(Native.EdsGetLength(stream, out var length));
            if (length > 4 * 1024 * 1024)
                throw new InvalidDataException("EVF frame too large");
            Check(Native.EdsGetPointer(stream, out var ptr));
            var bytes = new byte[(int)length];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            Frame?.Invoke(bytes);
        }
        finally { if (image != IntPtr.Zero) Native.EdsRelease(image); if (stream != IntPtr.Zero) Native.EdsRelease(stream); }
    }
    public async Task<string> Capture(string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Invoke(() => { if (camera == IntPtr.Zero) throw new InvalidOperationException("Camera disconnected"); if (capture != null) throw new InvalidOperationException("Camera busy"); destination = path; capture = completion; try { Check(Native.EdsSendCommand(camera, 0, 0)); } catch { capture = null; destination = null; throw; } });
        try
        {
            return await completion.Task.WaitAsync(token);
        }
        catch
        {
            await Invoke(() =>
            {
                if (capture == completion)
                {
                    capture = null;
                    destination = null; // Reopen before another capture so late object events cannot become the next shot.
                    DisconnectCore();
                }
            });
            throw;
        }
    }
    uint ObjectEvent(uint ev, IntPtr item, IntPtr context)
    {
        try
        {
            if (ev != 0x208)
                return 0;
            Check(Native.EdsGetDirectoryItemInfo(item, out var info));
            if (capture == null || destination == null || !info.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                Native.EdsDownloadCancel(item);
                return 0;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + ".partial";
            IntPtr stream = IntPtr.Zero;
            try
            {
                Check(Native.EdsCreateFileStream(temp, 1, 2, out stream));
                Check(Native.EdsDownload(item, info.Size, stream));
                Check(Native.EdsDownloadComplete(item));
                Native.EdsRelease(stream);
                stream = IntPtr.Zero;
                File.Move(temp, destination, true);
                var done = capture;
                var file = destination;
                capture = null;
                destination = null;
                done.TrySetResult(file);
                Photo?.Invoke(file);
            }
            catch { Native.EdsDownloadCancel(item); throw; }
            finally { if (stream != IntPtr.Zero) Native.EdsRelease(stream); if (File.Exists(temp)) File.Delete(temp); }
        }
        catch (Exception e) { capture?.TrySetException(e); capture = null; destination = null; Error?.Invoke(e.Message); }
        finally { if (item != IntPtr.Zero) Native.EdsRelease(item); }
        return 0;
    }
    uint StateEvent(uint ev, uint param, IntPtr context)
    {
        if (ev == 0x301)
        {
            live = false;
            capture?.TrySetException(new IOException("Camera disconnected"));
            capture = null;
            destination = null;
            var old = camera;
            camera = IntPtr.Zero;
            if (old != IntPtr.Zero)
                Native.EdsRelease(old);
            Disconnected?.Invoke();
        }
        else if (ev == 0x303 && camera != IntPtr.Zero)
            Native.EdsSendCommand(camera, 1, 0);
        return 0;
    }
    void DisconnectCore()
    {
        StopLiveCore();
        capture?.TrySetException(new IOException("Camera disconnected"));
        capture = null;
        destination = null;
        if (camera != IntPtr.Zero)
        {
            Native.EdsCloseSession(camera);
            Native.EdsRelease(camera);
            camera = IntPtr.Zero;
        }
        Disconnected?.Invoke();
    }
    public Task Disconnect() => Invoke(DisconnectCore);
    public async Task<CameraStatus> Status()
    {
        CameraStatus? status = null;
        await Invoke(() => status = new(camera != IntPtr.Zero, live, "canon", model, error));
        return status!;
    }
    public async ValueTask DisposeAsync()
    {
        await Invoke(() => { DisconnectCore(); if (initialized) { Native.EdsTerminateSDK(); initialized = false; } stop = true; });
        worker.Join(TimeSpan.FromSeconds(3));
        commands.Dispose();
    }

    static class Native
    {
        const string Dll = "EDSDK.dll";
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint ObjectHandler(uint ev, IntPtr item, IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint StateHandler(uint ev, uint param, IntPtr context);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Port; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public uint SubType; public uint Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct Capacity
        {
            public int NumberOfFreeClusters; public int BytesPerSector; public int Reset;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DirectoryInfo
        {
            public ulong Size; public int IsFolder; public uint GroupId; public uint Option; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string FileName; public uint Format; public uint DateTime;
        }
        [DllImport(Dll)] public static extern uint EdsInitializeSDK(); [DllImport(Dll)] public static extern uint EdsTerminateSDK(); [DllImport(Dll)] public static extern uint EdsGetEvent();
        [DllImport(Dll)] public static extern uint EdsGetCameraList(out IntPtr list); [DllImport(Dll)] public static extern uint EdsGetChildCount(IntPtr parent, out int count); [DllImport(Dll)] public static extern uint EdsGetChildAtIndex(IntPtr parent, int index, out IntPtr child); [DllImport(Dll)] public static extern uint EdsGetDeviceInfo(IntPtr camera, out DeviceInfo info);
        [DllImport(Dll)] public static extern uint EdsOpenSession(IntPtr camera); [DllImport(Dll)] public static extern uint EdsCloseSession(IntPtr camera); [DllImport(Dll)] public static extern uint EdsRelease(IntPtr reference);
        [DllImport(Dll)] public static extern uint EdsSetPropertyData(IntPtr reference, uint property, int param, uint size, ref uint data); [DllImport(Dll)] public static extern uint EdsGetPropertyData(IntPtr reference, uint property, int param, uint size, out uint data); [DllImport(Dll)] public static extern uint EdsSetCapacity(IntPtr camera, Capacity capacity);
        [DllImport(Dll)] public static extern uint EdsSetObjectEventHandler(IntPtr camera, uint ev, ObjectHandler handler, IntPtr context); [DllImport(Dll)] public static extern uint EdsSetCameraStateEventHandler(IntPtr camera, uint ev, StateHandler handler, IntPtr context);
        [DllImport(Dll)] public static extern uint EdsSendCommand(IntPtr camera, uint command, int param); [DllImport(Dll)] public static extern uint EdsCreateMemoryStream(ulong size, out IntPtr stream); [DllImport(Dll)] public static extern uint EdsCreateEvfImageRef(IntPtr stream, out IntPtr image); [DllImport(Dll)] public static extern uint EdsDownloadEvfImage(IntPtr camera, IntPtr image); [DllImport(Dll)] public static extern uint EdsGetLength(IntPtr stream, out ulong length); [DllImport(Dll)] public static extern uint EdsGetPointer(IntPtr stream, out IntPtr ptr);
        [DllImport(Dll)] public static extern uint EdsGetDirectoryItemInfo(IntPtr item, out DirectoryInfo info); [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern uint EdsCreateFileStream(string file, uint disposition, uint access, out IntPtr stream); [DllImport(Dll)] public static extern uint EdsDownload(IntPtr item, ulong size, IntPtr stream); [DllImport(Dll)] public static extern uint EdsDownloadComplete(IntPtr item); [DllImport(Dll)] public static extern uint EdsDownloadCancel(IntPtr item);
    }
}
