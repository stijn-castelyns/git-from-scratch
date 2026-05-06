# Building Git from Scratch — Live Demo Snippets

Copy-paste snippets keyed to the `live-demo` branch commits. Start from **Step 0** (`1206c1c`) and apply each step in order to reach the final state.

---

## Starting point — Step 0 (`1206c1c`)

The presenter starts with a minimal .NET console skeleton:

- `GitFromScratch/GitFromScratch.csproj` — project configured as a single-file exe named `lit`, Spectre.Console CLI package referenced
- `GitFromScratch/Program.cs` — empty `CommandApp` with a placeholder `hello` command
- `GitFromScratch/Utils/BinaryExtensions.cs` — big-endian read/write helpers (needed later by the index)
- `GitFromScratch/Properties/PublishProfiles/FolderProfile.pubxml`
- `GitFromScratch.slnx`, `.gitattributes`, `.gitignore`

No code additions needed at Step 0 — the skeleton is already on disk.

---

## Step 1: Repo initialization (`b5c6f67`)

Create the `.git` directory, implement `Repository.Init` / `Repository.Open`, and wire up the `init` command.

### `GitFromScratch/Repository.cs` *(new file)*

```csharp
namespace GitFromScratch;

internal class Repository
{
    public string WorkDir { get; }
    public string GitDir { get; set; }
    public string ObjectsDir { get; set; }

    private Repository(string workDir)
    {
        WorkDir = Path.GetFullPath(workDir);
        GitDir = Path.Combine(workDir, ".git");
        ObjectsDir = Path.Combine(GitDir, "objects");
    }

    public static Repository Init(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string gitDir = Path.Combine(fullPath, ".git");

        Directory.CreateDirectory(Path.Combine(gitDir, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));

        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(gitDir, "config"), """
            [core]
                repositoryformatversion = 0
                filemode = false
                bare = false
            """);

        return new Repository(fullPath);
    }

    public static Repository Open(string path)
    {
        DirectoryInfo? dir = new DirectoryInfo(Path.GetFullPath(path));

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return new Repository(dir.FullName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("fatal: not a git repository (or any parent directories): .git");
    }
}
```

### `GitFromScratch/Commands/InitRepoCommand.cs` *(new file)*

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class InitRepoCommand : Command<InitRepoCommand.Settings>
{

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        public string Path { get; set; } = ".";
    }
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Init(settings.Path);
        AnsiConsole.MarkupLine($"Initialized empty repository in [blue]{repo.GitDir}[/]");

        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — replace placeholder with init)*

```csharp
using GitFromScratch.Commands;
using Spectre.Console.Cli;

CommandApp? app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<InitRepoCommand>("init");
});

return app.Run(args);
```

---

## Step 2: Blob hashing and storage (`00dee5e`)

Introduce the `GitObject` base class with SHA-1 hashing + zlib storage, add `GitBlob`, and wire up `hash-object`.

### `GitFromScratch/Models/GitObject.cs` *(new file)*

```csharp
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
}
```

### `GitFromScratch/Models/GitBlob.cs` *(new file)*

```csharp
namespace GitFromScratch.Models;

public class GitBlob : GitObject
{
    public override string Type => "blob";
    public byte[] Data { get; }

    public GitBlob(byte[] data)
    {
        Data = data;
    }

    public override byte[] Serialize() => Data;
}
```

### `GitFromScratch/WorkingTree.cs` *(new file)*

```csharp
using System.Text;

namespace GitFromScratch;

internal class WorkingTree
{
    public static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
```

### `GitFromScratch/Repository.cs` *(modified — add HashObject)*

Add `using GitFromScratch.Models;` at the top, then append to the class:

```csharp
using GitFromScratch.Models;

namespace GitFromScratch;

internal class Repository
{
    // ... existing code ...

    public GitBlob HashObject(string filePath, bool write = false)
    {
        byte[] fileContent = WorkingTree.NormalizeLineEndings(File.ReadAllBytes(filePath));

        GitBlob gitBlob = new GitBlob(fileContent);

        if (write) gitBlob.Write(ObjectsDir);
        return gitBlob;
    }
}
```

### `GitFromScratch/Commands/HashObjectCommand.cs` *(new file)*

```csharp
using GitFromScratch.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class HashObjectCommand : Command<HashObjectCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        public string? FilePath { get; set; }
        [CommandOption("-w|--write")]
        public bool WriteObject { get; set; } = false;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath);

        GitObject gitObject = repo.HashObject(settings.FilePath, write: settings.WriteObject);

        AnsiConsole.WriteLine(gitObject.Sha);

        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register hash-object)*

```csharp
    config.AddCommand<InitRepoCommand>("init");
    config.AddCommand<HashObjectCommand>("hash-object");
});
```

---

## Step 3: The index and `add` command (`9d6bd29`)

Implement the binary index format and the `add` command that stages files.

### `GitFromScratch/Staging/GitIndexEntry.cs` *(new file)*

```csharp
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
    public uint Mode { get; set; }
    public uint Uid { get; set; }
    public uint Gid { get; set; }
    public uint FileSize { get; set; }
    public byte[] Sha { get; set; }
    public string Path { get; set; }

    /// <summary>
    /// Stage: 0 = normal, 1 = base, 2 = ours, 3 = theirs (during merge conflicts)
    /// </summary>
    public int Stage { get; set; }

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
```

### `GitFromScratch/Staging/GitIndex.cs` *(new file)*

```csharp
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
}
```

### `GitFromScratch/Repository.cs` *(modified — add staging)*

Add `using GitFromScratch.Staging;` and append the `Add` method:

```csharp
using GitFromScratch.Models;
using GitFromScratch.Staging;

// ... inside Repository ...

    public void Add(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        IEnumerable<string> files = Directory.Exists(fullPath)
            ? Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            : [fullPath];

        GitIndex index = new GitIndex(GitDir);
        foreach (string file in files)
        {
            string relativePath = Path.GetRelativePath(WorkDir, file).Replace('\\', '/');
            GitBlob blob = HashObject(file, write: true);
            index.Add(relativePath, blob, new FileInfo(file));
        }

        index.SortEntries();
        index.Save();
    }
```

### `GitFromScratch/Commands/AddCommand.cs` *(new file)*

```csharp
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class AddCommand : Command<AddCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        public string? FilePath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath!);
        repo.Add(settings.FilePath!);
        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register add)*

```csharp
    config.AddCommand<HashObjectCommand>("hash-object");
    config.AddCommand<AddCommand>("add");
});
```

---

## Step 4: Tree creation from the index (`fc99232`)

Add tree objects, tree-from-index building, and a `write-tree` command. Also teach `GitObject` to *read* objects from disk.

### `GitFromScratch/Models/GitTree.cs` *(new file)*

```csharp
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch.Models;

public record GitTreeEntry(string Mode, string Name, string Sha);

public class GitTree : GitObject
{
    public override string Type => "tree";
    public List<GitTreeEntry> Entries { get; } = new();

    public GitTree() { }

    /// <summary>
    /// Constructs a GitTree by parsing raw binary content (used by GitObject.Read).
    /// Format per entry: "<mode> <name>\0<20-byte-sha>"
    /// </summary>
    public GitTree(byte[] content)
    {
        int i = 0;
        while (i < content.Length)
        {
            int spaceIdx = Array.IndexOf(content, (byte)' ', i);
            string mode = Encoding.ASCII.GetString(content, i, spaceIdx - i);

            int nullIdx = Array.IndexOf(content, (byte)0, spaceIdx + 1);
            string name = Encoding.ASCII.GetString(content, spaceIdx + 1, nullIdx - spaceIdx - 1);

            byte[] shaBytes = content[(nullIdx + 1)..(nullIdx + 21)];
            string sha = Convert.ToHexString(shaBytes).ToLower();

            Entries.Add(new GitTreeEntry(mode, name, sha));
            i = nullIdx + 21;
        }
    }

    public static GitTree FromIndex(GitIndex index, string objectsDir) =>
        FromEntries(index.Entries.Select(e => (e.Path, Convert.ToHexString(e.Sha).ToLower())), objectsDir);

    private static GitTree FromEntries(IEnumerable<(string path, string sha)> entries, string objectsDir)
    {
        GitTree tree = new GitTree();

        IEnumerable<IGrouping<string?, (string path, string sha)>> grouped = entries.GroupBy(e =>
        {
            int slash = e.path.IndexOf('/');
            return slash == -1 ? null : e.path[..slash];
        });

        foreach (IGrouping<string?, (string path, string sha)> group in grouped)
        {
            if (group.Key is null)
            {
                foreach ((string path, string sha) entry in group)
                    tree.Entries.Add(new GitTreeEntry("100644", entry.path, entry.sha));
            }
            else
            {
                IEnumerable<(string path, string sha)> subEntries = group.Select(e => (e.path[(group.Key.Length + 1)..], e.sha));
                GitTree subTree = FromEntries(subEntries, objectsDir);
                tree.Entries.Add(new GitTreeEntry("40000", group.Key, subTree.Sha));
            }
        }

        tree.Write(objectsDir);
        return tree;
    }

    public override byte[] Serialize()
    {
        IOrderedEnumerable<GitTreeEntry> sorted = Entries.OrderBy(e => e.Name, StringComparer.Ordinal);
        using MemoryStream ms = new MemoryStream();

        foreach (GitTreeEntry treeEntry in sorted)
        {
            byte[] header = Encoding.ASCII.GetBytes($"{treeEntry.Mode} {treeEntry.Name}\0");
            byte[] sha = Convert.FromHexString(treeEntry.Sha);
            ms.Write(header);
            ms.Write(sha);
        }

        return ms.ToArray();
    }
}
```

### `GitFromScratch/Models/GitObject.cs` *(modified — add Read)*

Append to the `GitObject` class:

```csharp
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
            _ => throw new NotSupportedException($"Git object type '{type}' is not supported.")
        };

        obj.Sha = sha;
        return obj;
    }
}
```

### `GitFromScratch/Commands/WriteTreeCommand.cs` *(new file)*

```csharp
using GitFromScratch.Models;
using GitFromScratch.Staging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class WriteTreeCommand : Command<WriteTreeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        public string? FilePath { get; set; } = ".";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath);

        GitIndex index = new GitIndex(repo.GitDir);

        GitTree tree = GitTree.FromIndex(index, repo.ObjectsDir);

        tree.Write(repo.ObjectsDir);

        AnsiConsole.WriteLine(tree.Sha);

        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register write-tree)*

```csharp
    config.AddCommand<AddCommand>("add");
    config.AddCommand<WriteTreeCommand>("write-tree");
});
```

---

## Step 5: Commit creation (`bb9da56`)

Add commit objects, HEAD/ref management, and the `commit` command.

### `GitFromScratch/Models/GitCommit.cs` *(new file)*

```csharp
using System.Text;

namespace GitFromScratch.Models;

public class GitCommit : GitObject
{
    public override string Type => "commit";
    public string TreeSha { get; }
    public List<string> Parents { get; } = new();
    public string Author { get; }
    public string Committer { get; }
    public string Message { get; }

    private readonly long _timestamp;
    private readonly byte[] _rawContent;

    public GitCommit(string treeSha, IEnumerable<string> parents, string author, string committer, string message)
    {
        TreeSha = treeSha;
        Parents = parents.ToList();
        Author = author;
        Committer = committer;
        Message = message;
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _rawContent = BuildContent();
    }

    /// <summary>
    /// Constructs a GitCommit by parsing raw content bytes (used by GitObject.Read).
    /// </summary>
    public GitCommit(byte[] content)
    {
        _rawContent = content;
        string[] parts = Encoding.UTF8.GetString(content).Split("\n\n", 2);
        Message = parts.Length > 1 ? parts[1].TrimEnd('\n') : "";

        foreach (string line in parts[0].Split('\n'))
        {
            if (line.StartsWith("tree ")) TreeSha = line[5..];
            else if (line.StartsWith("parent ")) Parents.Add(line[7..]);
            else if (line.StartsWith("author ")) Author = line[7..];
            else if (line.StartsWith("committer ")) Committer = line[10..];
        }

        TreeSha ??= "";
        Author ??= "";
        Committer ??= "";
    }

    public override byte[] Serialize() => _rawContent;

    private byte[] BuildContent()
    {
        string parents = string.Concat(Parents.Select(p => $"parent {p}\n"));
        return Encoding.UTF8.GetBytes(
            $"tree {TreeSha}\n{parents}" +
            $"author {Author} {_timestamp} +0000\n" +
            $"committer {Committer} {_timestamp} +0000\n" +
            $"\n{Message}\n");
    }
}
```

### `GitFromScratch/Models/GitObject.cs` *(modified — add commit type to switch)*

```csharp
            "blob" => new GitBlob(content),
            "tree" => new GitTree(content),
            "commit" => new GitCommit(content),
            _ => throw new NotSupportedException($"Git object type '{type}' is not supported.")
        };
```

### `GitFromScratch/ReferenceManager.cs` *(new file)*

```csharp
namespace GitFromScratch;

internal class ReferenceManager(string gitDir)
{
    public string? ResolveHead()
    {
        string head = ReadHead();
        return head.StartsWith("ref: ") ? ReadRef(head[5..]) : head;
    }

    public void UpdateHead(string commitSha)
    {
        string head = ReadHead();
        if (!head.StartsWith("ref: "))
            throw new InvalidOperationException("HEAD is detached; cannot update ref.");

        WriteRef(head[5..], commitSha);
    }

    public string GetCurrentBranch()
    {
        string head = ReadHead();
        if (head.StartsWith("ref: refs/heads/"))
            return head["ref: refs/heads/".Length..];
        throw new InvalidOperationException("HEAD is detached.");
    }

    private string ReadHead() => File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();

    private string? ReadRef(string relativePath)
    {
        string refPath = RefPath(relativePath);
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
    }

    private void WriteRef(string relativePath, string commitSha)
    {
        string refPath = RefPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    private string RefPath(string relativePath) =>
        Path.Combine(gitDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
```

### `GitFromScratch/Repository.cs` *(modified — add Commit)*

Append to the class:

```csharp
    public string Commit(string message)
    {
        GitIndex index = new GitIndex(GitDir);
        if (index.Entries.Count == 0)
            throw new InvalidOperationException("nothing to commit");

        string treeSha = GitTree.FromIndex(index, ObjectsDir).Sha;
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        List<string> parents = parentSha is not null ? [parentSha] : [];

        if (parentSha is not null
            && GitObject.Read(parentSha, ObjectsDir) is GitCommit parentCommit
            && parentCommit.TreeSha == treeSha)
            throw new InvalidOperationException("nothing to commit, working tree clean");

        GitCommit commit = new GitCommit(
            treeSha: treeSha,
            parents: parents,
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: message);

        commit.Write(ObjectsDir);
        refs.UpdateHead(commit.Sha);

        return commit.Sha;
    }
```

### `GitFromScratch/Commands/CommitCommand.cs` *(new file)*

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class CommitCommand : Command<CommitCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-m|--message")]
        public string? Message { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Message))
        {
            AnsiConsole.MarkupLine("[red]error: commit message required (-m)[/]");
            return 1;
        }

        Repository repo = Repository.Open(".");
        string sha = repo.Commit(settings.Message);

        ReferenceManager refs = new ReferenceManager(repo.GitDir);
        string branch = refs.GetCurrentBranch();

        AnsiConsole.MarkupLine($"[green][[{branch} {sha[..7]}]][/] {settings.Message}");
        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register commit)*

```csharp
    config.AddCommand<WriteTreeCommand>("write-tree");
    config.AddCommand<CommitCommand>("commit");
});
```

---

## Step 6: Branch creation (`d83c8c6`)

A branch is just a file under `refs/heads/` containing a commit SHA.

### `GitFromScratch/ReferenceManager.cs` *(modified — add CreateBranch)*

```csharp
        throw new InvalidOperationException("HEAD is detached.");
    }

    public void CreateBranch(string branchName)
    {
        string commitSha = ResolveHead()
            ?? throw new InvalidOperationException("fatal: not a valid object name: 'HEAD'");

        if (BranchExists(branchName))
            throw new InvalidOperationException($"fatal: a branch named '{branchName}' already exists");

        WriteRef($"refs/heads/{branchName}", commitSha);
    }

    public bool BranchExists(string branchName) => File.Exists(RefPath($"refs/heads/{branchName}"));

    private string ReadHead() => File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
```

### `GitFromScratch/Commands/BranchCommand.cs` *(new file)*

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class BranchCommand : Command<BranchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");
        ReferenceManager refs = new ReferenceManager(repo.GitDir);

        refs.CreateBranch(settings.Name);

        AnsiConsole.MarkupLine($"[green]Created branch '{settings.Name}'[/]");
        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register branch)*

```csharp
    config.AddCommand<CommitCommand>("commit");
    config.AddCommand<BranchCommand>("branch");
});
```

---

## Step 7: Checkout (`a1e2f01`)

Replace working-tree files with the target branch's tree, rebuild the index, and move HEAD.

### `GitFromScratch/Models/GitTree.cs` *(modified — add Flatten)*

Insert before `Serialize()`:

```csharp
        return tree;
    }

    public static IEnumerable<GitTreeEntry> Flatten(string treeSha, string objectsDir, string prefix = "")
    {
        if (GitObject.Read(treeSha, objectsDir) is not GitTree tree)
            throw new InvalidOperationException("fatal: expected tree object");

        foreach (GitTreeEntry entry in tree.Entries)
        {
            string fullPath = prefix == "" ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry.Mode == "40000")
                foreach (GitTreeEntry sub in Flatten(entry.Sha, objectsDir, fullPath))
                    yield return sub;
            else
                yield return entry with { Name = fullPath };
        }
    }

    public override byte[] Serialize()
```

### `GitFromScratch/ReferenceManager.cs` *(modified — branch lookup + SetHead)*

Insert after `CreateBranch`:

```csharp
        WriteRef($"refs/heads/{branchName}", commitSha);
    }

    public bool BranchExists(string branchName) => File.Exists(RefPath($"refs/heads/{branchName}"));

    public string? ResolveBranch(string branchName) => ReadRef($"refs/heads/{branchName}");

    public void SetHead(string branchName) =>
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/{branchName}\n");

    private string ReadHead() => File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
```

### `GitFromScratch/WorkingTree.cs` *(modified — promote to instance class)*

Replace the existing file body with the expanded version:

```csharp
using GitFromScratch.Models;
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch;

internal class WorkingTree(string workDir, string gitDir, string objectsDir)
{
    public static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }

    public void DeleteFromWorkTree(string path)
    {
        string filePath = ToWorkTreePath(path);
        if (File.Exists(filePath))
            File.Delete(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir != workDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public void AddToIndexAndWorkTree(GitIndex index, string path, string blobSha)
    {
        if (GitObject.Read(blobSha, objectsDir) is not GitBlob blob)
            throw new InvalidOperationException($"fatal: expected blob for {path}");

        string filePath = ToWorkTreePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, blob.Data);
        index.Add(path, blob, new FileInfo(filePath));
    }

    public void CheckoutTree(string commitSha)
    {
        GitIndex index = new GitIndex(gitDir);
        foreach (GitIndexEntry entry in index.Entries)
            DeleteFromWorkTree(entry.Path);
        index.Entries.Clear();

        if (GitObject.Read(commitSha, objectsDir) is not GitCommit target)
            throw new InvalidOperationException("fatal: not a commit object");

        foreach (GitTreeEntry treeEntry in GitTree.Flatten(target.TreeSha, objectsDir))
            AddToIndexAndWorkTree(index, treeEntry.Name, treeEntry.Sha);

        index.SortEntries();
        index.Save();
    }

    public string ToWorkTreePath(string gitPath) =>
        Path.Combine(workDir, gitPath.Replace('/', Path.DirectorySeparatorChar));
}
```

### `GitFromScratch/Repository.cs` *(modified — construct WorkingTree + add Checkout)*

Add a field and instantiate in the constructor:

```csharp
    public string WorkDir { get; }
    public string GitDir { get; set; }
    public string ObjectsDir { get; set; }
    private readonly WorkingTree _workingTree;

    private Repository(string workDir)
    {
        WorkDir = Path.GetFullPath(workDir);
        GitDir = Path.Combine(workDir, ".git");
        ObjectsDir = Path.Combine(GitDir, "objects");
        _workingTree = new WorkingTree(WorkDir, GitDir, ObjectsDir);
    }
```

Append the `Checkout` method at the end of the class:

```csharp
    public void Checkout(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);

        string targetSha = refs.ResolveBranch(branchName)
            ?? throw new InvalidOperationException($"error: pathspec '{branchName}' did not match any branch known to lit");
        if (refs.GetCurrentBranch() == branchName)
            throw new InvalidOperationException($"Already on '{branchName}'");

        _workingTree.CheckoutTree(targetSha);
        refs.SetHead(branchName);
    }
```

### `GitFromScratch/Commands/CheckoutCommand.cs` *(new file)*

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class CheckoutCommand : Command<CheckoutCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<branch>")]
        public string Branch { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");

        repo.Checkout(settings.Branch);

        AnsiConsole.MarkupLine($"[green]Switched to branch '{settings.Branch}'[/]");
        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register checkout)*

```csharp
    config.AddCommand<BranchCommand>("branch");
    config.AddCommand<CheckoutCommand>("checkout");
});
```

---

## Step 8: Fast-forward merge (`c6126ce`)

Introduce the merge engine with merge-base detection and the fast-forward path only.

### `GitFromScratch/MergeEngine.cs` *(new file)*

```csharp
using GitFromScratch.Models;

namespace GitFromScratch;

internal enum MergeResult
{
    FastForward
}

internal class MergeEngine(string objectsDir, WorkingTree workingTree)
{
    public MergeResult? TryFastForward(ReferenceManager refs, string branchName)
    {
        if (!refs.BranchExists(branchName))
            throw new InvalidOperationException($"merge: '{branchName}' - not something we can merge");
        if (refs.GetCurrentBranch() == branchName)
            throw new InvalidOperationException("Already up to date.");

        string? oursSha = refs.ResolveHead();
        string? theirsSha = refs.ResolveBranch(branchName)
            ?? throw new InvalidOperationException("Already up to date.");

        if (oursSha is null)
        {
            refs.UpdateHead(theirsSha);
            workingTree.CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (oursSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        string? baseSha = FindMergeBase(oursSha, theirsSha);

        if (baseSha == oursSha)
        {
            refs.UpdateHead(theirsSha);
            workingTree.CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (baseSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        return null;
    }

    public string? FindMergeBase(string sha1, string sha2)
    {
        HashSet<string> ancestors = Ancestors(sha1);
        Queue<string> queue = new([sha2]);
        HashSet<string> visited = [sha2];

        while (queue.TryDequeue(out string? current))
        {
            if (ancestors.Contains(current)) return current;
            if (GitObject.Read(current, objectsDir) is GitCommit commit)
                foreach (string parent in commit.Parents)
                    if (visited.Add(parent)) queue.Enqueue(parent);
        }

        return null;
    }

    private HashSet<string> Ancestors(string sha)
    {
        HashSet<string> set = [];
        Queue<string> queue = new([sha]);
        while (queue.TryDequeue(out string? current))
        {
            if (!set.Add(current)) continue;
            if (GitObject.Read(current, objectsDir) is GitCommit commit)
                foreach (string parent in commit.Parents) queue.Enqueue(parent);
        }
        return set;
    }
}
```

### `GitFromScratch/Repository.cs` *(modified — add Merge)*

Append at the end of the class:

```csharp
    public MergeResult Merge(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);
        MergeEngine merger = new MergeEngine(ObjectsDir, _workingTree);

        MergeResult? fastForwardResult = merger.TryFastForward(refs, branchName);
        if (fastForwardResult is not null)
            return fastForwardResult.Value;

        throw new NotImplementedException("TO-DO: Implement merge logic for non-fast-forward cases");
    }
```

### `GitFromScratch/Commands/MergeCommand.cs` *(new file)*

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class MergeCommand : Command<MergeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<branch>")]
        public string Branch { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");

        MergeResult result = repo.Merge(settings.Branch);

        switch (result)
        {
            case MergeResult.FastForward:
                AnsiConsole.MarkupLine($"[green]Fast-forward[/]");
                break;
        }

        return 0;
    }
}
```

### `GitFromScratch/Program.cs` *(modified — register merge)*

```csharp
    config.AddCommand<CheckoutCommand>("checkout");
    config.AddCommand<MergeCommand>("merge");
});
```

---

## Step 9: Three-way merge + conflicts (`9611a23`)

Replace the `NotImplementedException` with the real three-way merge: file-by-file resolution, conflict markers, `MERGE_HEAD`, and merge commits with two parents.

### `GitFromScratch/MergeEngine.cs` *(modified — expand enums, add resolution logic)*

Replace the top of the file (using + enum) with:

```csharp
using GitFromScratch.Models;
using GitFromScratch.Staging;

namespace GitFromScratch;


internal enum MergeResult
{
    FastForward,
    Merged,
    Conflict
}

internal enum MergeAction
{
    Keep,
    TakeOurs,
    TakeTheirs,
    Delete,
    Conflict
}

internal record MergeFileResult(
    string Path,
    MergeAction Action,
    string? BlobSha,
    string? BaseSha,
    string? OursSha,
    string? TheirsSha);

internal class MergeEngine(string objectsDir, WorkingTree workingTree)
```

Append at the end of the `MergeEngine` class (after `FindMergeBase`):

```csharp
    public List<MergeFileResult> ResolveFiles(
        Dictionary<string, string> baseFiles,
        Dictionary<string, string> oursFiles,
        Dictionary<string, string> theirsFiles)
    {
        HashSet<string> allPaths = new(baseFiles.Keys);
        allPaths.UnionWith(oursFiles.Keys);
        allPaths.UnionWith(theirsFiles.Keys);

        List<MergeFileResult> results = new();

        foreach (string path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            baseFiles.TryGetValue(path, out string? baseSha);
            oursFiles.TryGetValue(path, out string? oursSha);
            theirsFiles.TryGetValue(path, out string? theirsSha);

            results.Add(ResolveFile(path, baseSha, oursSha, theirsSha));
        }

        return results;
    }

    private static MergeFileResult ResolveFile(string path, string? baseSha, string? oursSha, string? theirsSha)
    {
        (MergeAction action, string? blobSha) = (baseSha, oursSha, theirsSha) switch
        {
            _ when oursSha == theirsSha => (MergeAction.Keep, oursSha),
            _ when oursSha == baseSha => (theirsSha is not null ? MergeAction.TakeTheirs : MergeAction.Delete, theirsSha),
            _ when theirsSha == baseSha => (oursSha is not null ? MergeAction.TakeOurs : MergeAction.Delete, oursSha),
            _ => (MergeAction.Conflict, null)
        };

        return new MergeFileResult(path, action, blobSha, baseSha, oursSha, theirsSha);
    }

    public Dictionary<string, string> GetTreeFiles(string? commitSha)
    {
        if (commitSha is null) return new();
        if (GitObject.Read(commitSha, objectsDir) is not GitCommit commit) return new();
        return GitTree.Flatten(commit.TreeSha, objectsDir).ToDictionary(e => e.Name, e => e.Sha);
    }

    public void CreateMergeCommit(ReferenceManager refs, string treeSha, string oursSha, string theirsSha, string branchName)
    {
        GitCommit mergeCommit = new GitCommit(
            treeSha: treeSha,
            parents: [oursSha, theirsSha],
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: $"Merge branch '{branchName}'");
        mergeCommit.Write(objectsDir);
        refs.UpdateHead(mergeCommit.Sha);
    }

    public List<string> ApplyMergeResults(GitIndex index, List<MergeFileResult> results, string currentBranch, string theirsBranch)
    {
        List<string> conflicts = new();

        foreach (MergeFileResult result in results)
        {
            switch (result.Action)
            {
                case MergeAction.Keep:
                case MergeAction.TakeOurs:
                case MergeAction.TakeTheirs:
                    if (result.BlobSha is not null)
                        workingTree.AddToIndexAndWorkTree(index, result.Path, result.BlobSha);
                    break;

                case MergeAction.Delete:
                    workingTree.DeleteFromWorkTree(result.Path);
                    break;

                case MergeAction.Conflict:
                    conflicts.Add(result.Path);
                    workingTree.WriteConflictMarkers(result.Path, result.OursSha, result.TheirsSha, currentBranch, theirsBranch);

                    if (result.BaseSha is not null)
                        index.AddAtStage(result.Path, result.BaseSha, 1);
                    if (result.OursSha is not null)
                        index.AddAtStage(result.Path, result.OursSha, 2);
                    if (result.TheirsSha is not null)
                        index.AddAtStage(result.Path, result.TheirsSha, 3);
                    break;
            }
        }

        return conflicts;
    }
```

### `GitFromScratch/Staging/GitIndex.cs` *(modified — add AddAtStage)*

Append inside the class:

```csharp
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
```

### `GitFromScratch/WorkingTree.cs` *(modified — add WriteConflictMarkers)*

Insert after `CheckoutTree`:

```csharp
        index.Save();
    }

    public void WriteConflictMarkers(string path, string? oursSha, string? theirsSha, string currentBranch, string theirsBranch)
    {
        string oursContent = oursSha is not null ? ReadBlobContent(oursSha) : "";
        string theirsContent = theirsSha is not null ? ReadBlobContent(theirsSha) : "";

        StringBuilder sb = new();
        sb.AppendLine($"<<<<<<< {currentBranch}");
        sb.Append(oursContent);
        if (!oursContent.EndsWith('\n')) sb.AppendLine();
        sb.AppendLine("=======");
        sb.Append(theirsContent);
        if (!theirsContent.EndsWith('\n')) sb.AppendLine();
        sb.AppendLine($">>>>>>> {theirsBranch}");

        string filePath = ToWorkTreePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
    }

    public string ToWorkTreePath(string gitPath) =>
        Path.Combine(workDir, gitPath.Replace('/', Path.DirectorySeparatorChar));

    private string ReadBlobContent(string sha) =>
        GitObject.Read(sha, objectsDir) is GitBlob blob ? Encoding.UTF8.GetString(blob.Data) : "";
}
```

### `GitFromScratch/Repository.cs` *(modified — wire merge commits + MERGE_HEAD into Commit and Merge)*

Update `Commit` to support merge commits (add second parent, skip empty-tree guard, clean up `MERGE_HEAD`):

```csharp
        List<string> parents = parentSha is not null ? [parentSha] : [];

        string mergeHeadPath = Path.Combine(GitDir, "MERGE_HEAD");
        if (File.Exists(mergeHeadPath))
            parents.Add(File.ReadAllText(mergeHeadPath).Trim());

        if (parentSha is not null && !File.Exists(mergeHeadPath)
            && GitObject.Read(parentSha, ObjectsDir) is GitCommit parentCommit
            && parentCommit.TreeSha == treeSha)
            throw new InvalidOperationException("nothing to commit, working tree clean");

        GitCommit commit = new GitCommit(
            treeSha: treeSha,
            parents: parents,
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: message);
        commit.Write(ObjectsDir);
        refs.UpdateHead(commit.Sha);

        if (File.Exists(mergeHeadPath))
            File.Delete(mergeHeadPath);

        return commit.Sha;
    }
```

Replace the `NotImplementedException` in `Merge` with the three-way merge flow:

```csharp
        MergeResult? fastForwardResult = merger.TryFastForward(refs, branchName);
        if (fastForwardResult is not null)
            return fastForwardResult.Value;


        string currentBranch = refs.GetCurrentBranch();
        string oursSha = refs.ResolveHead()!;
        string theirsSha = refs.ResolveBranch(branchName)!;
        string? baseSha = merger.FindMergeBase(oursSha, theirsSha);

        List<MergeFileResult> results = merger.ResolveFiles(
            baseFiles: merger.GetTreeFiles(baseSha),
            oursFiles: merger.GetTreeFiles(oursSha),
            theirsFiles: merger.GetTreeFiles(theirsSha));

        GitIndex index = new GitIndex(GitDir);
        index.Entries.Clear();
        List<string> conflicts = merger.ApplyMergeResults(index, results, currentBranch, branchName);

        index.SortEntries();
        index.Save();

        if (conflicts.Count > 0)
        {
            File.WriteAllText(Path.Combine(GitDir, "MERGE_HEAD"), theirsSha + "\n");
            return MergeResult.Conflict;
        }

        string treeSha = GitTree.FromIndex(index, ObjectsDir).Sha;
        merger.CreateMergeCommit(refs, treeSha, oursSha, theirsSha, branchName);
        return MergeResult.Merged;
    }
```

### `GitFromScratch/Commands/MergeCommand.cs` *(modified — handle all three results)*

Extend the switch:

```csharp
            case MergeResult.FastForward:
                AnsiConsole.MarkupLine($"[green]Fast-forward[/]");
                break;
            case MergeResult.Merged:
                AnsiConsole.MarkupLine($"[green]Merge made by the 'ort' strategy.[/]");
                break;
            case MergeResult.Conflict:
                AnsiConsole.MarkupLine($"[yellow]Auto-merging failed; fix conflicts and then commit the result.[/]");
                return 1;
        }

        return 0;
```
