using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GitFromScratch.Models;

public abstract class GitObject
{
    public abstract string Type { get; }
    public string Sha { get; protected set; }
    public abstract byte[] Serialize();

    /// <summary>
    /// Combines the Git header and the serialized content into a single payload.
    /// </summary>
    private byte[] GetRawPayload()
    {
        byte[] content = Serialize();
        byte[] header = Encoding.ASCII.GetBytes($"{Type} {content.Length}\0");

        return [.. header, .. content];
    }

    public string ComputeHash()
    {
        if (Sha != null) return Sha;

        byte[] raw = GetRawPayload();
        Sha = Convert.ToHexString(SHA1.HashData(raw)).ToLower();
        return Sha;
    }

    public string Write(string objectsDir)
    {
        // 1. Get the payload exactly once
        byte[] raw = GetRawPayload();

        // 2. Compute the hash from the payload if we don't have it yet
        Sha ??= Convert.ToHexString(SHA1.HashData(raw)).ToLower();

        // 3. Determine paths
        string dir = Path.Combine(objectsDir, Sha[..2]);
        string file = Path.Combine(dir, Sha[2..]);

        if (File.Exists(file))
            return Sha;

        Directory.CreateDirectory(dir);

        // 4. Compress and write
        using FileStream fs = File.Create(file);
        using ZLibStream zlib = new ZLibStream(fs, CompressionLevel.Optimal);
        zlib.Write(raw);

        return Sha;
    }
}
