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
}
