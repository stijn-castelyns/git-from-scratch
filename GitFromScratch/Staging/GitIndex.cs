using GitFromScratch.Models;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GitFromScratch.Staging;

public class GitIndex
{
    private readonly string _indexPath;
    public List<GitIndexEntry> Entries { get; set; } = new List<GitIndexEntry>();

    public GitIndex(string gitDir)
    {
        _indexPath = Path.Combine(gitDir, "index");
        Load();
    }

    // ──────────────────────── ADD / STAGE ─────────────────────────

    public void Add(string relativePath, GitBlob blob, FileInfo fileInfo)
    {
        Entries.RemoveAll(e => e.Path == relativePath);

        DateTime epoch = DateTime.UnixEpoch;
        Entries.Add(new GitIndexEntry
        {
            CTimeSec = (uint)(fileInfo.CreationTimeUtc - epoch).TotalSeconds,
            MTimeSec = (uint)(fileInfo.LastWriteTimeUtc - epoch).TotalSeconds,
            Mode = 0x81A4,
            FileSize = (uint)fileInfo.Length,
            Sha = Convert.FromHexString(blob.ComputeHash()),
            Path = relativePath,
            Stage = 0
        });
        SortEntries();
        Save();
    }

    // ──────────────────────── CONFLICT STAGING ────────────────────

    public void AddConflict(
        string relativePath,
        string baseSha,
        string oursSha,
        string theirsSha)
    {
        Entries.RemoveAll(e => e.Path == relativePath);
        if (baseSha != null) AddAtStage(relativePath, baseSha, stage: 1);
        if (oursSha != null) AddAtStage(relativePath, oursSha, stage: 2);
        if (theirsSha != null) AddAtStage(relativePath, theirsSha, stage: 3);
        SortEntries();
        Save();
    }

    public void ResolveConflict(string relativePath, GitBlob resolvedBlob, FileInfo fi)
    {
        Entries.RemoveAll(e => e.Path == relativePath);
        Add(relativePath, resolvedBlob, fi); // Add already calls Save()
    }

    public bool HasConflicts() => Entries.Any(e => e.Stage != 0);

    public List<string> GetConflictedPaths() =>
        Entries.Where(e => e.Stage != 0).Select(e => e.Path).Distinct().ToList();

    private void AddAtStage(string path, string sha, int stage)
    {
        Entries.Add(new GitIndexEntry
        {
            Mode = 0x81A4,
            Sha = Convert.FromHexString(sha),
            Path = path,
            Stage = stage
        });
    }

    private void SortEntries()
    {
        Entries = Entries
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ThenBy(e => e.Stage)
            .ToList();
    }

    // ──────────────────────── BINARY WRITE ────────────────────────

    /// <summary>
    /// Writes Git's binary index format (version 2).
    /// Layout: "DIRC" | version(4) | count(4) | entries... | SHA-1 checksum(20)
    /// </summary>
    private void Save()
    {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        bw.Write(Encoding.ASCII.GetBytes("DIRC"));
        bw.WriteUInt32BE(2);
        bw.WriteUInt32BE((uint)Entries.Count);

        foreach (GitIndexEntry entry in Entries)
        {
            bw.WriteUInt32BE(entry.CTimeSec);
            bw.WriteUInt32BE(entry.CTimeNano);
            bw.WriteUInt32BE(entry.MTimeSec);
            bw.WriteUInt32BE(entry.MTimeNano);
            bw.WriteUInt32BE(entry.Dev);
            bw.WriteUInt32BE(entry.Ino);
            bw.WriteUInt32BE(entry.Mode);
            bw.WriteUInt32BE(entry.Uid);
            bw.WriteUInt32BE(entry.Gid);
            bw.WriteUInt32BE(entry.FileSize);
            bw.Write(entry.Sha);                   // 20 raw bytes
            bw.WriteUInt16BE(entry.ComputeFlags()); // stage + name length

            byte[] pathBytes = Encoding.UTF8.GetBytes(entry.Path);
            bw.Write(pathBytes);
            bw.Write((byte)0);  // null terminator

            // Pad to 8-byte boundary
            int entryLen = 62 + pathBytes.Length + 1;
            int padding = (8 - (entryLen % 8)) % 8;
            for (int i = 0; i < padding; i++)
                bw.Write((byte)0);
        }

        byte[] data = ms.ToArray();
        byte[] checksum = SHA1.HashData(data);

        using FileStream fs = File.Create(_indexPath);
        fs.Write(data);
        fs.Write(checksum);
    }

    // ──────────────────────── BINARY READ ─────────────────────────

    private void Load()
    {
        if (!File.Exists(_indexPath)) { Entries = new(); return; }

        byte[] data = File.ReadAllBytes(_indexPath);
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader br = new BinaryReader(ms);

        string sig = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (sig != "DIRC") throw new InvalidDataException("Invalid index signature");

        uint version = br.ReadUInt32BE();
        if (version != 2) throw new InvalidDataException($"Unsupported index version: {version}");

        uint count = br.ReadUInt32BE();
        Entries = new List<GitIndexEntry>((int)count);

        for (uint idx = 0; idx < count; idx++)
        {
            long entryStart = ms.Position;

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

            long consumed = ms.Position - entryStart;
            int pad = (int)((8 - (consumed % 8)) % 8);
            if (pad > 0) br.ReadBytes(pad);

            Entries.Add(entry);
        }
    }
}

// ──────────────────────── BIG-ENDIAN EXTENSIONS ───────────────────

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
