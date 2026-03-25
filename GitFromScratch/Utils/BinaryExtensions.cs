using System.Buffers.Binary;

namespace GitFromScratch.Utils;

public static class BinaryExtensions
{
    public static void WriteUInt32BE(this BinaryWriter bw, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        bw.Write(buf);
    }
    public static void WriteUInt16BE(this BinaryWriter bw, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, value);
        bw.Write(buf);
    }
    public static uint ReadUInt32BE(this BinaryReader br)
    {
        Span<byte> buf = stackalloc byte[4];
        br.BaseStream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }
    public static ushort ReadUInt16BE(this BinaryReader br)
    {
        Span<byte> buf = stackalloc byte[2];
        br.BaseStream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt16BigEndian(buf);
    }
}
