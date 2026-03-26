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

        // Resolve parent commit(s) from HEAD
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        List<string> parents = new();
        if (parentSha is not null)
            parents.Add(parentSha);

        // Check for MERGE_HEAD (set during merge with conflicts)
        string mergeHeadPath = Path.Combine(GitDir, "MERGE_HEAD");
        if (File.Exists(mergeHeadPath))
        {
            string mergeHead = File.ReadAllText(mergeHeadPath).Trim();
            parents.Add(mergeHead);
        }

        // Abort if the tree is identical to the parent commit's tree
        if (parentSha is not null && !File.Exists(mergeHeadPath))
        {
            GitObject parentObj = GitObject.Read(parentSha, ObjectsDir);
            if (parentObj is GitCommit parentCommit && parentCommit.TreeSha == treeSha)
                throw new InvalidOperationException("nothing to commit, working tree clean");
        }

        // Create and write the commit object
        GitCommit commit = new GitCommit(
            treeSha: treeSha,
            parents: parents,
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: message
        );
        commit.Write(ObjectsDir);

        // Update the branch ref
        refs.UpdateHead(commit.Sha);

        // Clean up merge state
        if (File.Exists(mergeHeadPath))
            File.Delete(mergeHeadPath);

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

        CheckoutTree(targetSha);
        refs.SetHead(branchName);
    }

    public enum MergeResult { FastForward, Merged, Conflict }

    public MergeResult Merge(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);
        string currentBranch = refs.GetCurrentBranch();

        MergeResult? fastForwardResult = TryFastForward(refs, branchName);
        if (fastForwardResult is not null)
            return fastForwardResult.Value;

        string oursSha = refs.ResolveHead()!;
        string theirsSha = refs.ResolveBranch(branchName)!;
        string? baseSha = FindMergeBase(oursSha, theirsSha);

        List<MergeFileResult> results = ThreeWayMerge.ResolveFiles(
            baseFiles: GetTreeFiles(baseSha),
            oursFiles: GetTreeFiles(oursSha),
            theirsFiles: GetTreeFiles(theirsSha));

        GitIndex index = new GitIndex(GitDir);
        index.Entries.Clear();
        List<string> conflicts = ApplyMergeResults(index, results, currentBranch, branchName);

        index.SortEntries();
        index.Save();

        if (conflicts.Count > 0)
        {
            File.WriteAllText(Path.Combine(GitDir, "MERGE_HEAD"), theirsSha + "\n");
            return MergeResult.Conflict;
        }

        CreateMergeCommit(refs, index, oursSha, theirsSha, branchName);
        return MergeResult.Merged;
    }

    private MergeResult? TryFastForward(ReferenceManager refs, string branchName)
    {
        if (!refs.BranchExists(branchName))
            throw new InvalidOperationException($"merge: '{branchName}' - not something we can merge");

        if (refs.GetCurrentBranch() == branchName)
            throw new InvalidOperationException("Already up to date.");

        string? oursSha = refs.ResolveHead();
        string? theirsSha = refs.ResolveBranch(branchName);

        if (theirsSha is null)
            throw new InvalidOperationException("Already up to date.");

        if (oursSha is null)
        {
            refs.UpdateHead(theirsSha);
            CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (oursSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        string? baseSha = FindMergeBase(oursSha, theirsSha);

        if (baseSha == oursSha)
        {
            refs.UpdateHead(theirsSha);
            CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (baseSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        return null; // Needs three-way merge
    }

    private List<string> ApplyMergeResults(GitIndex index, List<MergeFileResult> results, string currentBranch, string theirsBranch)
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
                        AddToIndexAndWorkTree(index, result.Path, result.BlobSha);
                    break;

                case MergeAction.Delete:
                    DeleteFromWorkTree(result.Path);
                    break;

                case MergeAction.Conflict:
                    conflicts.Add(result.Path);
                    WriteConflictMarkers(result.Path, result.BaseSha, result.OursSha, result.TheirsSha, currentBranch, theirsBranch);

                    if (result.BaseSha is not null)
                        AddAtStageToIndex(index, result.Path, result.BaseSha, 1);
                    if (result.OursSha is not null)
                        AddAtStageToIndex(index, result.Path, result.OursSha, 2);
                    if (result.TheirsSha is not null)
                        AddAtStageToIndex(index, result.Path, result.TheirsSha, 3);
                    break;
            }
        }

        return conflicts;
    }

    private void CreateMergeCommit(ReferenceManager refs, GitIndex index, string oursSha, string theirsSha, string branchName)
    {
        IEnumerable<FileEntry> entries = index.Entries.Select(e =>
            new FileEntry(e.Path, Convert.ToHexString(e.Sha).ToLower()));
        string treeSha = BuildTreeFromIndex(entries);

        GitCommit mergeCommit = new GitCommit(
            treeSha: treeSha,
            parents: new[] { oursSha, theirsSha },
            author: "Lit User <lit@example.com>",
            committer: "Lit User <lit@example.com>",
            message: $"Merge branch '{branchName}'"
        );
        mergeCommit.Write(ObjectsDir);
        refs.UpdateHead(mergeCommit.Sha);
    }

    private string? FindMergeBase(string sha1, string sha2)
    {
        HashSet<string> ancestors1 = new();
        HashSet<string> ancestors2 = new();
        Queue<string> queue1 = new();
        Queue<string> queue2 = new();

        queue1.Enqueue(sha1);
        queue2.Enqueue(sha2);
        ancestors1.Add(sha1);
        ancestors2.Add(sha2);

        while (queue1.Count > 0 || queue2.Count > 0)
        {
            if (queue1.Count > 0)
            {
                string current = queue1.Dequeue();
                if (ancestors2.Contains(current))
                    return current;

                GitObject obj = GitObject.Read(current, ObjectsDir);
                if (obj is GitCommit commit)
                {
                    foreach (string parent in commit.Parents)
                    {
                        if (ancestors1.Add(parent))
                            queue1.Enqueue(parent);
                    }
                }
            }

            if (queue2.Count > 0)
            {
                string current = queue2.Dequeue();
                if (ancestors1.Contains(current))
                    return current;

                GitObject obj = GitObject.Read(current, ObjectsDir);
                if (obj is GitCommit commit)
                {
                    foreach (string parent in commit.Parents)
                    {
                        if (ancestors2.Add(parent))
                            queue2.Enqueue(parent);
                    }
                }
            }
        }

        return null;
    }

    private Dictionary<string, string> GetTreeFiles(string? commitSha)
    {
        if (commitSha is null)
            return new Dictionary<string, string>();

        GitObject obj = GitObject.Read(commitSha, ObjectsDir);
        if (obj is not GitCommit commit)
            return new Dictionary<string, string>();

        List<GitTreeEntry> entries = GitTree.Flatten(commit.TreeSha, ObjectsDir);
        return entries.ToDictionary(e => e.Name, e => e.Sha);
    }

    private void AddToIndexAndWorkTree(GitIndex index, string path, string blobSha)
    {
        GitObject obj = GitObject.Read(blobSha, ObjectsDir);
        if (obj is not GitBlob blob)
            throw new InvalidOperationException($"fatal: expected blob for {path}");

        string filePath = Path.Combine(WorkDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, blob.Data);

        FileInfo fi = new FileInfo(filePath);
        index.Add(path, blob, fi);
    }

    private void AddAtStageToIndex(GitIndex index, string path, string sha, int stage)
    {
        index.Entries.Add(new GitIndexEntry
        {
            Mode = 0x81A4,
            Sha = Convert.FromHexString(sha),
            Path = path,
            Stage = stage
        });
    }

    private void DeleteFromWorkTree(string path)
    {
        string filePath = Path.Combine(WorkDir, path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
            File.Delete(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir != WorkDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    private void WriteConflictMarkers(string path, string? baseSha, string? oursSha, string? theirsSha, string currentBranch, string theirsBranch)
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

        string filePath = Path.Combine(WorkDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
    }

    private string ReadBlobContent(string sha)
    {
        GitObject obj = GitObject.Read(sha, ObjectsDir);
        if (obj is not GitBlob blob)
            return "";
        return Encoding.UTF8.GetString(blob.Data);
    }

    private void CheckoutTree(string commitSha)
    {
        // Remove current tracked files
        GitIndex currentIndex = new GitIndex(GitDir);
        foreach (GitIndexEntry entry in currentIndex.Entries)
        {
            string filePath = Path.Combine(WorkDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath))
                File.Delete(filePath);

            string? dir = Path.GetDirectoryName(filePath);
            while (dir != null && dir != WorkDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        // Write target tree files
        GitObject commitObj = GitObject.Read(commitSha, ObjectsDir);
        if (commitObj is not GitCommit targetCommit)
            throw new InvalidOperationException("fatal: not a commit object");

        List<GitTreeEntry> targetFiles = GitTree.Flatten(targetCommit.TreeSha, ObjectsDir);
        GitIndex newIndex = new GitIndex(GitDir);
        newIndex.Entries.Clear();

        foreach (GitTreeEntry treeEntry in targetFiles)
        {
            GitObject blobObj = GitObject.Read(treeEntry.Sha, ObjectsDir);
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
    }

    private static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
