using System.Buffers.Binary;
namespace Booth.Camera.Sony;
public static class SonyProperties
{
    public static ulong Read(byte[] data, ushort wanted)
    {
        int offset = 8;
        if (data.Length < offset) throw new InvalidDataException("Sony property header truncated");
        while (offset < data.Length)
        {
            Require(data, offset, 6);
            var code = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
            var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2)); offset += 6;
            Value(data, ref offset, type); var current = Value(data, ref offset, type);
            if (code == wanted) return current;
            Require(data, offset, 1); var form = data[offset++];
            if (form == 1) { for (var i = 0; i < 3; i++) Value(data, ref offset, type); }
            else if (form == 2) { Require(data, offset, 2); int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)); offset += 2; for (var i = 0; i < count; i++) Value(data, ref offset, type); }
            else if (form != 0) throw new InvalidDataException($"Unknown Sony property form {form}");
        }
        throw new InvalidDataException($"Sony property 0x{wanted:X4} not available");
    }
    static ulong Value(byte[] data, ref int offset, ushort type)
    {
        if (type == 0xffff) { Require(data, offset, 1); int size = data[offset++] * 2; Require(data, offset, size); offset += size; return 0; }
        if ((type & 0x4000) != 0) { Require(data, offset, 4); uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)); offset += 4; if (count > 100000) throw new InvalidDataException("Sony array too large"); for (var i = 0; i < count; i++) Value(data, ref offset, (ushort)(type & ~0x4000)); return 0; }
        int width = type switch { 1 or 2 => 1, 3 or 4 => 2, 5 or 6 => 4, 7 or 8 => 8, 9 or 10 => 16, _ => throw new InvalidDataException($"Sony property type 0x{type:X4} unsupported") };
        Require(data, offset, width); ulong value = 0; for (int i = 0; i < Math.Min(width, 8); i++) value |= (ulong)data[offset + i] << (8 * i); offset += width; return value;
    }
    static void Require(byte[] data, int offset, int length) { if (offset < 0 || length < 0 || offset > data.Length - length) throw new InvalidDataException("Sony property payload truncated"); }
}
