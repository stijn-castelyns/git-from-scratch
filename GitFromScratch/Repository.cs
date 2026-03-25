using GitFromScratch.Models;
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch;

internal class Repository
{
    private record FileEntry(string Path, string Sha);

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
            gitObject.Write(ObjectsDir);
        }

        return gitObject;
    }

    public string Commit(string message)
    {
        GitIndex index = new GitIndex(GitDir);

        if (index.Entries.Count == 0)
            throw new InvalidOperationException("nothing to commit");

        // Build tree hierarchy from flat index entries
        IEnumerable<FileEntry> entries = index.Entries.Select(e =>
            new FileEntry(e.Path, Convert.ToHexString(e.Sha).ToLower()));
        string treeSha = BuildTreeFromIndex(entries);

        // Resolve parent commit from HEAD
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        // Abort if the tree is identical to the parent commit's tree
        if (parentSha is not null)
        {
            GitObject parentObj = GitObject.Read(parentSha, ObjectsDir);
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
        commit.Write(ObjectsDir);

        // Update the branch ref
        refs.UpdateHead(commit.Sha);

        return commit.Sha;
    }

    private string BuildTreeFromIndex(IEnumerable<FileEntry> entries)
    {
        GitTree tree = new GitTree();

        // Separate files in this directory from files in subdirectories
        IEnumerable<IGrouping<string?, FileEntry>> grouped = entries.GroupBy(e =>
        {
            int slash = e.Path.IndexOf('/');
            return slash == -1 ? (string?)null : e.Path[..slash];
        });

        foreach (IGrouping<string?, FileEntry> group in grouped)
        {
            if (group.Key is null)
            {
                // Direct file children
                foreach (FileEntry entry in group)
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
                IEnumerable<FileEntry> subEntries = group.Select(e => new FileEntry(e.Path[(group.Key.Length + 1)..], e.Sha));
                string subTreeSha = BuildTreeFromIndex(subEntries);

                tree.Entries.Add(new GitTreeEntry
                {
                    Mode = "40000",
                    Name = group.Key,
                    Sha = subTreeSha
                });
            }
        }

        tree.Write(ObjectsDir);
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

    public void Checkout(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);

        if (!refs.BranchExists(branchName))
            throw new InvalidOperationException($"error: pathspec '{branchName}' did not match any branch known to lit");

        string currentBranch = refs.GetCurrentBranch();
        if (currentBranch == branchName)
            throw new InvalidOperationException($"Already on '{branchName}'");

        string? targetSha = refs.ResolveBranch(branchName);
        if (targetSha is null)
            throw new InvalidOperationException($"fatal: branch '{branchName}' has no commits");

        string objectsDir = Path.Combine(GitDir, "objects");

        // Read the target commit's tree
        GitObject commitObj = GitObject.Read(targetSha, objectsDir);
        if (commitObj is not GitCommit targetCommit)
            throw new InvalidOperationException("fatal: not a commit object");

        // Collect all files from the target tree
        List<GitTreeEntry> targetFiles = GitTree.Flatten(targetCommit.TreeSha, objectsDir);

        // Remove files tracked by the current index
        GitIndex currentIndex = new GitIndex(GitDir);
        foreach (GitIndexEntry entry in currentIndex.Entries)
        {
            string filePath = Path.Combine(WorkDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Clean up empty directories
            string? dir = Path.GetDirectoryName(filePath);
            while (dir != null && dir != WorkDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        // Write all files from the target tree to the working directory
        GitIndex newIndex = new GitIndex(GitDir);
        foreach (GitTreeEntry treeEntry in targetFiles)
        {
            GitObject blobObj = GitObject.Read(treeEntry.Sha, objectsDir);
            if (blobObj is not GitBlob blob)
                throw new InvalidOperationException($"fatal: expected blob for {treeEntry.Name}");

            string filePath = Path.Combine(WorkDir, treeEntry.Name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, blob.Data);

            FileInfo fi = new FileInfo(filePath);
            newIndex.Add(treeEntry.Name, blob, fi);
        }

        newIndex.SortEntries();
        newIndex.Save();

        // Update HEAD to point to the new branch
        refs.SetHead(branchName);
    }

    private static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
