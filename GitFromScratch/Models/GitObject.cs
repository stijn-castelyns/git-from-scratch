using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.Arm;
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

        // Using C# 12 collection expressions - perfect for a modern .NET talk!
        return [.. header, .. content];
    }

    public string ComputeHash()
    {
        if (Sha != null) return Sha; // Return cached hash if already computed

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
            return Sha;  // content-addressed: identical content = already stored

        Directory.CreateDirectory(dir);

        // 4. Compress and write
        using FileStream fs = File.Create(file);
        using ZLibStream zlib = new ZLibStream(fs, CompressionLevel.Optimal);
        zlib.Write(raw);

        return Sha;
    }

    /// <summary>
    /// Reads and decompresses a Git object from disk, returning the appropriate subclass.
    /// </summary>
    public static GitObject Read(string sha, string objectsDir)
    {
        string dir = Path.Combine(objectsDir, sha[..2]);
        string file = Path.Combine(dir, sha[2..]);

        if (!File.Exists(file))
            throw new FileNotFoundException($"Git object {sha} not found.");

        // 1. Read and decompress the entire file
        using FileStream fs = File.OpenRead(file);
        using ZLibStream zlib = new ZLibStream(fs, CompressionMode.Decompress);
        using MemoryStream ms = new MemoryStream();
        zlib.CopyTo(ms);
        
        byte[] raw = ms.ToArray();

        // 2. Find the null byte '\0' that separates the header from the content
        int nullIdx = Array.IndexOf(raw, (byte)0);
        if (nullIdx == -1)
            throw new InvalidDataException("Invalid Git object: missing null terminator.");

        // 3. Parse the header (e.g., "blob 14")
        string header = Encoding.ASCII.GetString(raw, 0, nullIdx);
        string[] parts = header.Split(' ');
        string type = parts[0];

        // 4. Extract just the content using C# range operators
        byte[] content = raw[(nullIdx + 1)..];

        // 5. Instantiate the correct subclass
        GitObject obj = type switch
        {
            "blob" => new GitBlob(content),
            // "tree" => new Tree(content),
            // "commit" => new Commit(content),
            _ => throw new NotSupportedException($"Git object type '{type}' is not supported.")
        };

        obj.Sha = sha;
        return obj;
    }
}
