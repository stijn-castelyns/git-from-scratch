using GitFromScratch.Models;
using GitFromScratch.Utils;
using System.Security.Cryptography;
using System.Text;

namespace GitFromScratch.Staging;

public class GitIndex(string gitDir)
{
    private readonly string _indexPath = Path.Combine(gitDir, "index");
    public List<GitIndexEntry> Entries { get; set; } = LoadEntries(Path.Combine(gitDir, "index"));

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
            Sha = Convert.FromHexString(blob.Sha),
            Path = relativePath,
            Stage = 0
        });
    }

    public void SortEntries()
    {
        Entries = Entries
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ThenBy(e => e.Stage)
            .ToList();
    }

    public void Save()
    {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        bw.Write(Encoding.ASCII.GetBytes("DIRC"));
        bw.WriteUInt32BE(2);
        bw.WriteUInt32BE((uint)Entries.Count);

        foreach (GitIndexEntry entry in Entries)
        {
            entry.WriteToIndex(bw);
        }

        byte[] data = ms.ToArray();
        byte[] checksum = SHA1.HashData(data);

        using FileStream fs = File.Create(_indexPath);
        fs.Write(data);
        fs.Write(checksum);
    }

    private static List<GitIndexEntry> LoadEntries(string indexPath)
    {
        if (!File.Exists(indexPath)) return new();

        byte[] data = File.ReadAllBytes(indexPath);
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader br = new BinaryReader(ms);

        string sig = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (sig != "DIRC") throw new InvalidDataException("Invalid index signature");

        uint version = br.ReadUInt32BE();
        if (version != 2) throw new InvalidDataException($"Unsupported index version: {version}");

        uint count = br.ReadUInt32BE();
        List<GitIndexEntry> entries = new((int)count);
        for (uint idx = 0; idx < count; idx++)
            entries.Add(GitIndexEntry.ReadFromIndex(br));
        return entries;
    }

    public void AddAtStage(string path, string sha, int stage)
    {
        Entries.Add(new GitIndexEntry
        {
            Mode = 0x81A4,
            Sha = Convert.FromHexString(sha),
            Path = path,
            Stage = stage
        });
    }
}
