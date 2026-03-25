using GitFromScratch.Models;
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch;

internal class Repository
{
    public string WorkDir { get; }
    public string GitDir { get; set; }

    private Repository(string workDir)
    {
        WorkDir = Path.GetFullPath(workDir);
        GitDir = Path.Combine(workDir, ".git");
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

    public GitObject HashObject(string filePath, string type = "blob", bool write = false)
    {
        byte[] fileContent = File.ReadAllBytes(filePath);

        // Git normalizes line endings (CRLF → LF) before hashing
        fileContent = NormalizeLineEndings(fileContent);

        GitObject gitObject = type switch
        {
            "blob" => new GitBlob(fileContent),
            _ => throw new InvalidOperationException()
        };

        if (write)
        {
            gitObject.Write(Path.Combine(GitDir, "objects"));
        }

        return gitObject;
    }

    public string Commit(string message)
    {
        GitIndex index = new GitIndex(GitDir);

        if (index.Entries.Count == 0)
            throw new InvalidOperationException("nothing to commit");

        string objectsDir = Path.Combine(GitDir, "objects");

        // Build tree hierarchy from flat index entries
        var entries = index.Entries.Select(e =>
            (Path: e.Path, Sha: Convert.ToHexString(e.Sha).ToLower()));
        string treeSha = BuildTreeFromIndex(entries, objectsDir);

        // Resolve parent commit from HEAD
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        // Abort if the tree is identical to the parent commit's tree
        if (parentSha is not null)
        {
            GitObject parentObj = GitObject.Read(parentSha, objectsDir);
            if (parentObj is GitCommit parentCommit && parentCommit.TreeSha == treeSha)
                throw new InvalidOperationException("nothing to commit, working tree clean");
        }

        // Create and write the commit object
        GitCommit commit = new GitCommit(
            treeSha: treeSha,
            parentSha: parentSha,
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: message
        );
        commit.Write(objectsDir);

        // Update the branch ref
        refs.UpdateHead(commit.Sha);

        return commit.Sha;
    }

    private string BuildTreeFromIndex(IEnumerable<(string Path, string Sha)> entries, string objectsDir)
    {
        GitTree tree = new GitTree();

        // Separate files in this directory from files in subdirectories
        var grouped = entries.GroupBy(e =>
        {
            int slash = e.Path.IndexOf('/');
            return slash == -1 ? (string?)null : e.Path[..slash];
        });

        foreach (var group in grouped)
        {
            if (group.Key is null)
            {
                // Direct file children
                foreach (var entry in group)
                {
                    tree.Entries.Add(new GitTreeEntry
                    {
                        Mode = "100644",
                        Name = entry.Path,
                        Sha = entry.Sha
                    });
                }
            }
            else
            {
                // Subdirectory — recurse with stripped paths
                var subEntries = group.Select(e => (Path: e.Path[(group.Key.Length + 1)..], e.Sha));
                string subTreeSha = BuildTreeFromIndex(subEntries, objectsDir);

                tree.Entries.Add(new GitTreeEntry
                {
                    Mode = "40000",
                    Name = group.Key,
                    Sha = subTreeSha
                });
            }
        }

        tree.Write(objectsDir);
        return tree.ComputeHash();
    }

    public void Add(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);

        IEnumerable<string> files;
        if (Directory.Exists(fullPath))
        {
            files = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);
        }
        else
        {
            files = [fullPath];
        }

        GitIndex index = new GitIndex(GitDir);

        foreach (string file in files)
        {
            string relativePath = Path.GetRelativePath(WorkDir, file).Replace('\\', '/');
            GitBlob blob = (GitBlob)HashObject(file, write: true);
            index.Add(relativePath, blob, new FileInfo(file));
        }

        index.SortEntries();
        index.Save();
    }

    private static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
