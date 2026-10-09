using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Text;

namespace Booth.Camera.Sony;

/// <summary>ILCE-7M3 PTP remote protocol over the Windows inbox WPD transport.
/// Command values/packet semantics are cross-checked against libgphoto2's Sony protocol implementation.
/// No libgphoto binary or Sony proprietary SDK is loaded.</summary>
public sealed class SonyWpdTransport : ISonyTransport
{
    readonly BlockingCollection<Action> queue = new();
    readonly Thread worker;
    WpdConnection? connection;
    CameraDevice? selected;
    bool live, disposed;
    DateTimeOffset opened;
    const uint PreviewHandle = 0xffffc002, CaptureHandle = 0xffffc001;
    public SonyWpdTransport()
    {
        worker = new Thread(() => { var hr = WpdConnection.CoInitializeEx(0, 0); try { foreach (var action in queue.GetConsumingEnumerable()) action(); } finally { connection?.Dispose(); if (hr >= 0) WpdConnection.CoUninitialize(); } }) { IsBackground = true, Name = "Sony WPD/PTP" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    Task<T> Invoke<T>(Func<T> action, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() => { try { token.ThrowIfCancellationRequested(); result.TrySetResult(action()); } catch (OperationCanceledException) { result.TrySetCanceled(token); } catch (Exception e) { result.TrySetException(e); } });
        return result.Task;
    }
    byte[] Read(ushort opcode, CancellationToken token, int max = 1024 * 1024, params uint[] parameters) => (connection ?? throw new IOException("Sony camera disconnected")).Execute(opcode, parameters, null, true, max, token);
    void Control(uint property, ushort value, CancellationToken token) => (connection ?? throw new IOException("Sony camera disconnected")).Execute(0x9207, [property], BitConverter.GetBytes(value), false, 0, token);
    public Task<IReadOnlyList<CameraDevice>> ScanAsync(CancellationToken token) => Invoke(WpdConnection.Scan, token);
    public async Task OpenAsync(CameraDevice device, CancellationToken token)
    {
        await Invoke(() =>
        {
            if (!device.Model.Contains("ILCE-7M3", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Only Sony ILCE-7M3 is supported by this transport");
            connection?.Dispose(); connection = new WpdConnection();
            try
            {
                connection.Open(device.Id);
                Read(0x9201, token, 1024 * 1024, 1, 0, 0);
                Read(0x9201, token, 1024 * 1024, 2, 0, 0);
                Read(0x9202, token, 1024 * 1024, 200);
                Read(0x9201, token, 1024 * 1024, 3, 0, 0);
                Read(0x9202, token, 1024 * 1024, 200);
                // Confirm protocol access before reporting Connected.
                Read(0x9209, token);
                selected = device; opened = DateTimeOffset.UtcNow; return true;
            }
            catch { connection.Dispose(); connection = null; selected = null; throw; }
        }, token);
    }
    public async Task CloseAsync() => await Invoke(() => { live = false; connection?.Dispose(); connection = null; selected = null; return true; });
    public Task<CameraDevice> GetDeviceInfoAsync(CancellationToken token) => Invoke(() => selected ?? throw new IOException("Sony camera disconnected"), token);
    public async Task StartLiveViewAsync(CancellationToken token) => await Invoke(() => { if (selected is null) throw new IOException("Sony camera disconnected"); live = true; return true; }, token);
    public async Task StopLiveViewAsync(CancellationToken token) => await Invoke(() => { live = false; return true; }, token);
    public Task<byte[]?> ReadLiveViewFrameAsync(CancellationToken token) => Invoke<byte[]?>(() =>
    {
        if (!live) return null;
        try { Read(0x9209, token); Read(0x1008, token, 65536, PreviewHandle); return ExtractPreview(Read(0x1009, token, 4 * 1024 * 1024, PreviewHandle)); }
        catch (SonyPtpException e) when (e.Response is 0x2009 or 0x200f or 0x2019) { return null; }
        // WPD maps a not-yet-ready virtual preview object to ERROR_GEN_FAILURE.
        // The provider bounds this retry window; it never substitutes another image.
        catch (System.Runtime.InteropServices.COMException e) when (e.HResult == unchecked((int)0x8007001f)) { return null; }
    }, token);
    public async Task AutoFocusAsync(CancellationToken token) => await Invoke(() => { Control(0xd2c1, 2, token); return true; }, token);
    public Task<SonyCapturedObject> CaptureAsync(CancellationToken token) => Invoke(() =>
    {
        while ((DateTimeOffset.UtcNow - opened).TotalSeconds < 3) Wait(100, token);
        var properties = Read(0x9209, token);
        if (SonyProperties.Read(properties, 0xd215) >= 0x8000) throw new InvalidOperationException("Sony has an earlier image in its transfer buffer. Reconnect USB before capturing; refusing to assign an old image to this session.");
        try
        {
            Control(0xd2c1, 2, token); Control(0xd2c2, 2, token);
            Wait(300, token);
        }
        finally
        {
            // Always release the physical shutter even when the caller cancels.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { Control(0xd2c2, 1, cleanup.Token); } finally { Control(0xd2c1, 1, cleanup.Token); }
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (SonyProperties.Read(Read(0x9209, token), 0xd215) >= 0x8000)
            {
                var info = Read(0x1008, token, 65536, CaptureHandle);
                if (info.Length < 53) throw new InvalidDataException("Incomplete Sony object info");
                var format = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(4));
                if (format != 0x3801) throw new InvalidOperationException("Sony did not return JPEG. Set File Format=JPEG and PC Remote Save Dest.=PC or PC+Camera, then reconnect.");
                var size = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(8));
                var chars = info[52]; var name = chars > 0 && info.Length >= 53 + chars * 2 ? Encoding.Unicode.GetString(info, 53, (chars - 1) * 2) : "sony.jpg";
                return new SonyCapturedObject(CaptureHandle, size, name);
            }
            Wait(100, token);
        }
    }, token);
    public Task<byte[]> DownloadCapturedJpegAsync(SonyCapturedObject photo, CancellationToken token) => Invoke(() =>
    {
        if (photo.Handle != CaptureHandle || photo.Size is 0 or > 80 * 1024 * 1024) throw new InvalidDataException("Invalid Sony still object");
        var data = Read(0x1009, token, 80 * 1024 * 1024, photo.Handle);
        if (data.Length != photo.Size || data.Length < 4 || data[0] != 255 || data[1] != 216 || data[^2] != 255 || data[^1] != 217) throw new InvalidDataException("Sony full JPEG is incomplete");
        return data;
    }, token);
    static void Wait(int milliseconds, CancellationToken token) { if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested(); }
    public static byte[] ExtractPreview(byte[] data)
    {
        if (data.Length < 6) throw new InvalidDataException("Sony preview header missing");
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (offset > data.Length - 2 || data[offset] != 255 || data[offset + 1] != 216) throw new InvalidDataException("Sony preview JPEG offset invalid");
        for (int end = data.Length - 2; end > offset; end--) if (data[end] == 255 && data[end + 1] == 217) return data[(int)offset..(end + 2)];
        throw new InvalidDataException("Sony preview JPEG truncated");
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; await CloseAsync(); disposed = true; queue.CompleteAdding(); await Task.Run(() => worker.Join()); queue.Dispose();
    }
}
