using GitFromScratch.Utils;
using System.Text;

namespace GitFromScratch.Staging;

public class GitIndexEntry
{
    public uint CTimeSec { get; set; }
    public uint CTimeNano { get; set; }
    public uint MTimeSec { get; set; }
    public uint MTimeNano { get; set; }
    public uint Dev { get; set; }
    public uint Ino { get; set; }
    public uint Mode { get; set; }        // 0x81A4 = octal 100644 (regular file)
    public uint Uid { get; set; }
    public uint Gid { get; set; }
    public uint FileSize { get; set; }
    public byte[] Sha { get; set; }       // 20 raw bytes
    public string Path { get; set; }      // relative from repo root, forward slashes

    // Stage: 0 = normal, 1 = base, 2 = ours, 3 = theirs (during merge conflicts)
    public int Stage { get; set; }

    /// <summary>
    /// Encodes flags: bits 12-13 = stage, bits 0-11 = name length (capped at 0xFFF).
    /// </summary>
    public ushort ComputeFlags()
    {
        ushort nameLen = (ushort)Math.Min(Path.Length, 0xFFF);
        ushort stage = (ushort)(Stage << 12);
        return (ushort)(stage | nameLen);
    }

    public static GitIndexEntry ReadFromIndex(BinaryReader br)
    {
        long entryStart = br.BaseStream.Position;

        GitIndexEntry entry = new GitIndexEntry
        {
            CTimeSec = br.ReadUInt32BE(),
            CTimeNano = br.ReadUInt32BE(),
            MTimeSec = br.ReadUInt32BE(),
            MTimeNano = br.ReadUInt32BE(),
            Dev = br.ReadUInt32BE(),
            Ino = br.ReadUInt32BE(),
            Mode = br.ReadUInt32BE(),
            Uid = br.ReadUInt32BE(),
            Gid = br.ReadUInt32BE(),
            FileSize = br.ReadUInt32BE(),
            Sha = br.ReadBytes(20)
        };

        ushort flags = br.ReadUInt16BE();
        entry.Stage = (flags >> 12) & 0x3;

        List<byte> pathBytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0) pathBytes.Add(b);
        entry.Path = Encoding.UTF8.GetString(pathBytes.ToArray());

        long consumed = br.BaseStream.Position - entryStart;
        int pad = (int)((8 - (consumed % 8)) % 8);
        if (pad > 0) br.ReadBytes(pad);

        return entry;
    }

    public void WriteToIndex(BinaryWriter bw)
    {
        bw.WriteUInt32BE(this.CTimeSec);
        bw.WriteUInt32BE(this.CTimeNano);
        bw.WriteUInt32BE(this.MTimeSec);
        bw.WriteUInt32BE(this.MTimeNano);
        bw.WriteUInt32BE(this.Dev);
        bw.WriteUInt32BE(this.Ino);
        bw.WriteUInt32BE(this.Mode);
        bw.WriteUInt32BE(this.Uid);
        bw.WriteUInt32BE(this.Gid);
        bw.WriteUInt32BE(this.FileSize);
        bw.Write(this.Sha);                   // 20 raw bytes
        bw.WriteUInt16BE(this.ComputeFlags()); // stage + name length

        byte[] pathBytes = Encoding.UTF8.GetBytes(this.Path);
        bw.Write(pathBytes);
        bw.Write((byte)0);  // null terminator

        // Pad to 8-byte boundary
        int entryLen = 62 + pathBytes.Length + 1;
        int padding = (8 - (entryLen % 8)) % 8;
        for (int i = 0; i < padding; i++)
            bw.Write((byte)0);
    }
}
