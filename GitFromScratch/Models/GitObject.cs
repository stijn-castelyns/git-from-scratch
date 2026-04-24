using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GitFromScratch.Models;

public abstract class GitObject
{
    private string? _sha;
    public abstract string Type { get; }
    public string Sha
    {
        get => _sha ??= Convert.ToHexString(SHA1.HashData(GetRawPayload())).ToLower();
        protected set => _sha = value;
    }
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

    public string Write(string objectsDir)
    {
        string dir = Path.Combine(objectsDir, Sha[..2]);
        string file = Path.Combine(dir, Sha[2..]);

        if (File.Exists(file))
            return Sha;

        Directory.CreateDirectory(dir);

        using FileStream fs = File.Create(file);
        using ZLibStream zlib = new ZLibStream(fs, CompressionLevel.Optimal);
        zlib.Write(GetRawPayload());

        return Sha;
    }

    /// <summary>
    /// Reads and decompresses a Git object from disk, returning the appropriate subclass.
    /// </summary>
    public static GitObject Read(string sha, string objectsDir)
    {
        string file = Path.Combine(objectsDir, sha[..2], sha[2..]);
        if (!File.Exists(file))
            throw new FileNotFoundException($"Git object {sha} not found.");

        using FileStream fs = File.OpenRead(file);
        using ZLibStream zlib = new ZLibStream(fs, CompressionMode.Decompress);
        using MemoryStream ms = new MemoryStream();
        zlib.CopyTo(ms);
        byte[] raw = ms.ToArray();

        int nullIdx = Array.IndexOf(raw, (byte)0);
        if (nullIdx == -1)
            throw new InvalidDataException("Invalid Git object: missing null terminator.");

        string type = Encoding.ASCII.GetString(raw, 0, nullIdx).Split(' ')[0];
        byte[] content = raw[(nullIdx + 1)..];

        GitObject obj = type switch
        {
            "blob" => new GitBlob(content),
            "tree" => new GitTree(content),
            "commit" => new GitCommit(content),
            _ => throw new NotSupportedException($"Git object type '{type}' is not supported.")
        };

        obj.Sha = sha;
        return obj;
    }
}
