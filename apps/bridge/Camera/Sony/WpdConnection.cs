using System.Runtime.InteropServices;

namespace Booth.Camera.Sony;

/// <summary>Windows inbox WPD MTP extensions. x64 ABI from PortableDeviceApi/Types and WpdMtpExtensions.h.
/// Owns COM references explicitly; no vendor DLL, driver replacement or Imaging Edge dependency.</summary>
public sealed unsafe class WpdConnection : IDisposable
{
    static readonly Guid Mtp = new("4d545058-1a2e-4106-a357-771e0819fc56");
    static readonly Guid Common = new("f0422a9c-5dc8-4440-b5bd-5df28835658a");
    static readonly Guid Client = new("204d9f0c-2292-4080-9f42-40664e70f859");
    nint device;
    readonly object lifetime = new();
    [StructLayout(LayoutKind.Sequential)] struct Key(Guid category, uint id) { public Guid Category = category; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public ulong Number;
        [FieldOffset(8)] public nint Pointer;
        [FieldOffset(16)] public nint Data;
    }
    static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    static nint Slot(nint obj, int slot) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);
    static nint Create(string clsid, string iid)
    {
        var c = new Guid(clsid); var i = new Guid(iid); Check(CoCreateInstance(ref c, 0, 1, ref i, out var value)); return value;
    }
    static void Release(nint value) { if (value != 0) Marshal.Release(value); }
    public static IReadOnlyList<CameraDevice> Scan()
    {
        int hr = CoInitializeEx(0, 0); bool uninitialize = hr >= 0;
        nint manager = 0, memory = 0; uint count = 0;
        try
        {
            manager = Create("0af10cec-2ecd-4b92-9581-34f6ae0637f3", "a1567595-4c2f-4574-a6fa-ecef917b9a40");
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(manager, 4))(manager));
            var get = (delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Slot(manager, 3);
            Check(get(manager, 0, &count)); if (count > 256) throw new IOException("Unexpected WPD device count");
            memory = Marshal.AllocCoTaskMem(checked((int)count * IntPtr.Size)); new Span<byte>((void*)memory, (int)count * IntPtr.Size).Clear();
            Check(get(manager, memory, &count));
            var result = new List<CameraDevice>();
            for (int index = 0; index < count; index++)
            {
                var id = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, index * IntPtr.Size))!;
                var name = DeviceString(manager, 5, id); var manufacturer = DeviceString(manager, 7, id);
                if ((manufacturer.Contains("Sony", StringComparison.OrdinalIgnoreCase) || id.Contains("vid_054c", StringComparison.OrdinalIgnoreCase)) && name.Contains("ILCE-7M3", StringComparison.OrdinalIgnoreCase))
                    result.Add(new(id, "sony", "Sony", "Sony A7 III / ILCE-7M3", "USB / PC Remote"));
            }
            return result;
        }
        finally
        {
            if (memory != 0) { for (int i = 0; i < count; i++) Marshal.FreeCoTaskMem(Marshal.ReadIntPtr(memory, i * IntPtr.Size)); Marshal.FreeCoTaskMem(memory); }
            Release(manager); if (uninitialize) CoUninitialize();
        }
    }
    static string DeviceString(nint manager, int slot, string id)
    {
        var call = (delegate* unmanaged[Stdcall]<nint, char*, char*, uint*, int>)Slot(manager, slot);
        fixed (char* identifier = id)
        {
            uint length = 0; Check(call(manager, identifier, null, &length));
            if (length > 65536) throw new IOException("Invalid WPD name length");
            var text = new char[length]; fixed (char* buffer = text) { Check(call(manager, identifier, buffer, &length)); return new string(buffer).TrimEnd('\0'); }
        }
    }
    public void Open(string id)
    {
        lock (lifetime)
        {
            if (device != 0) throw new InvalidOperationException("Sony device already open");
            device = Create("f7c0039a-4762-488a-b4b3-760ef9a1ba9b", "625e2df8-6392-4cf0-9ad1-3cfa5f17775c");
            try
            {
                using var client = new Values(); client.Text(Client, 2, "VINHUNI Photobooth"); client.UInt(Client, 3, 1); client.UInt(Client, 9, 0xc0000000); client.UInt(Client, 10, 0);
                fixed (char* identifier = id) Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Slot(device, 3))(device, identifier, client.Handle));
            }
            catch { Release(device); device = 0; throw; }
        }
    }
    public void Cancel() { lock (lifetime) { if (device != 0) ((delegate* unmanaged[Stdcall]<nint, int>)Slot(device, 7))(device); } }
    public byte[] Execute(ushort operation, uint[] parameters, byte[]? write, bool read, int maximumBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (device == 0) throw new IOException("Sony camera disconnected");
        using var cancel = token.Register(Cancel);
        using var command = Command(write is not null ? 14u : read ? 13u : 12u);
        command.UInt(Mtp, 1001, operation); command.Parameters(parameters);
        if (write is not null) command.ULong(Mtp, 1007, (ulong)write.Length);
        using var start = Send(command);
        if (!read && write is null) { Response(start, operation); return []; }
        var context = start.String(Mtp, 1006); using var output = new MemoryStream();
        try
        {
            if (write is not null)
            {
                using var chunk = Command(16); chunk.Text(Mtp, 1006, context); chunk.UInt(Mtp, 1010, (uint)write.Length); chunk.Buffer(Mtp, 1012, write);
                using var written = Send(chunk); if (written.Number(Mtp, 1011) != (ulong)write.Length) throw new IOException("Incomplete Sony PTP write");
            }
            else
            {
                var total = start.Number(Mtp, 1007); bool unknown = total == uint.MaxValue;
                if (!unknown && total > (ulong)maximumBytes) throw new InvalidDataException("Sony transfer exceeds allowed image size");
                while (unknown || (ulong)output.Length < total)
                {
                    token.ThrowIfCancellationRequested(); uint wanted = (uint)Math.Min(1024 * 1024, unknown ? maximumBytes - output.Length : (long)total - output.Length);
                    if (wanted == 0) throw new InvalidDataException("Sony transfer exceeds image size limit");
                    using var chunk = Command(15); chunk.Text(Mtp, 1006, context); chunk.UInt(Mtp, 1008, wanted); chunk.Buffer(Mtp, 1012, new byte[wanted]);
                    using var received = Send(chunk); var data = received.Bytes(Mtp, 1012); var actual = received.Number(Mtp, 1009);
                    if (actual > wanted || actual > (ulong)data.Length) throw new InvalidDataException("Invalid WPD read length");
                    output.Write(data, 0, (int)actual);
                    if (actual < wanted) { if (!unknown && (ulong)output.Length != total) throw new EndOfStreamException("Incomplete Sony JPEG transfer"); break; }
                }
            }
            using var end = Command(17); end.Text(Mtp, 1006, context); using var completed = Send(end); Response(completed, operation);
            token.ThrowIfCancellationRequested(); return output.ToArray();
        }
        catch { Cancel(); throw; }
    }
    static void Response(Values values, ushort operation)
    {
        var code = values.Number(Mtp, 1003); if (code != 0x2001) throw new SonyPtpException(operation, (ushort)code);
    }
    static Values Command(uint id) { var value = new Values(); value.Guid(Common, 1001, Mtp); value.UInt(Common, 1002, id); return value; }
    Values Send(Values command)
    {
        nint response = 0; Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(device, 4))(device, 0, command.Handle, &response));
        var values = new Values(response);
        try { Check(unchecked((int)values.Number(Common, 1003))); return values; } catch { values.Dispose(); throw; }
    }
    public void Dispose()
    {
        lock (lifetime) { if (device == 0) return; try { ((delegate* unmanaged[Stdcall]<nint, int>)Slot(device, 8))(device); } finally { Release(device); device = 0; } }
    }
    sealed class Values : IDisposable
    {
        public nint Handle { get; }
        public Values() : this(Create("0c15d503-d017-47ce-9016-7b3f978721cc", "6848f6f2-3155-4f86-b6f5-263eeeab3143")) { }
        public Values(nint pointer) => Handle = pointer;
        void Set(Guid category, uint id, Variant value) { var key = new Key(category, id); Check(((delegate* unmanaged[Stdcall]<nint, Key*, Variant*, int>)Slot(Handle, 5))(Handle, &key, &value)); }
        Variant Get(Guid category, uint id) { var key = new Key(category, id); Variant value = default; Check(((delegate* unmanaged[Stdcall]<nint, Key*, Variant*, int>)Slot(Handle, 6))(Handle, &key, &value)); return value; }
        public void UInt(Guid category, uint id, uint value) => Set(category, id, new() { Type = 19, Number = value });
        public void ULong(Guid category, uint id, ulong value) => Set(category, id, new() { Type = 21, Number = value });
        public void Guid(Guid category, uint id, Guid value) => Set(category, id, new() { Type = 72, Pointer = (nint)(&value) });
        public void Text(Guid category, uint id, string value) { fixed (char* text = value) Set(category, id, new() { Type = 31, Pointer = (nint)text }); }
        public void Buffer(Guid category, uint id, byte[] value) { fixed (byte* bytes = value) Set(category, id, new() { Type = 0x1011, Number = (uint)value.Length, Data = (nint)bytes }); }
        public ulong Number(Guid category, uint id) { var value = Get(category, id); try { if (value.Type is not (19 or 21 or 10)) throw new InvalidDataException("Expected WPD integer"); return value.Type == 21 ? value.Number : (uint)value.Number; } finally { PropVariantClear(ref value); } }
        public string String(Guid category, uint id) { var value = Get(category, id); try { if (value.Type != 31) throw new InvalidDataException("Expected WPD string"); return Marshal.PtrToStringUni(value.Pointer)!; } finally { PropVariantClear(ref value); } }
        public byte[] Bytes(Guid category, uint id) { var value = Get(category, id); try { if (value.Type != 0x1011 || value.Number > 1024 * 1024) throw new InvalidDataException("Invalid WPD byte vector"); var bytes = new byte[(int)value.Number]; Marshal.Copy(value.Data, bytes, 0, bytes.Length); return bytes; } finally { PropVariantClear(ref value); } }
        public void Parameters(uint[] parameters)
        {
            var collection = Create("08a99e2f-6d6d-4b80-af5a-baf2bcbe4cb9", "89b2e422-4f1b-4316-bcef-a44afea83eb3");
            try { foreach (var number in parameters) { Variant value = new() { Type = 19, Number = number }; Check(((delegate* unmanaged[Stdcall]<nint, Variant*, int>)Slot(collection, 5))(collection, &value)); } Set(Mtp, 1002, new() { Type = 13, Pointer = collection }); }
            finally { Release(collection); }
        }
        public void Dispose() => Release(Handle);
    }
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref Variant value);
    [DllImport("ole32.dll")] internal static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] internal static extern void CoUninitialize();
}
public sealed class SonyPtpException(ushort operation, ushort response) : IOException($"Sony PTP 0x{operation:X4} returned 0x{response:X4}")
{
    public ushort Response { get; } = response;
}
