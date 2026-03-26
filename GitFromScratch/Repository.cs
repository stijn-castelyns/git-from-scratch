using GitFromScratch.Models;
using GitFromScratch.Staging;

namespace GitFromScratch;

internal class Repository
{
    private record FileEntry(string Path, string Sha);

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
        byte[] fileContent = WorkingTree.NormalizeLineEndings(File.ReadAllBytes(filePath));

        GitObject gitObject = type switch
        {
            "blob" => new GitBlob(fileContent),
            _ => throw new InvalidOperationException()
        };

        if (write) gitObject.Write(ObjectsDir);
        return gitObject;
    }

    public string Commit(string message)
    {
        GitIndex index = new GitIndex(GitDir);
        if (index.Entries.Count == 0)
            throw new InvalidOperationException("nothing to commit");

        string treeSha = BuildTreeFromEntries(index);
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        List<string> parents = parentSha is not null ? [parentSha] : [];

        string mergeHeadPath = Path.Combine(GitDir, "MERGE_HEAD");
        if (File.Exists(mergeHeadPath))
            parents.Add(File.ReadAllText(mergeHeadPath).Trim());

        // Abort if tree is identical to parent's tree (nothing changed)
        if (parentSha is not null && !File.Exists(mergeHeadPath))
        {
            GitObject parentObj = GitObject.Read(parentSha, ObjectsDir);
            if (parentObj is GitCommit parentCommit && parentCommit.TreeSha == treeSha)
                throw new InvalidOperationException("nothing to commit, working tree clean");
        }

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
        if (refs.GetCurrentBranch() == branchName)
            throw new InvalidOperationException($"Already on '{branchName}'");

        string targetSha = refs.ResolveBranch(branchName)
            ?? throw new InvalidOperationException($"fatal: branch '{branchName}' has no commits");

        _workingTree.CheckoutTree(targetSha);
        refs.SetHead(branchName);
    }

    public MergeResult Merge(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);
        MergeEngine merger = new MergeEngine(GitDir, ObjectsDir, _workingTree);

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

        string treeSha = BuildTreeFromEntries(index);
        merger.CreateMergeCommit(refs, treeSha, oursSha, theirsSha, branchName);
        return MergeResult.Merged;
    }

    private string BuildTreeFromIndex(IEnumerable<FileEntry> entries)
    {
        GitTree tree = new GitTree();

        var grouped = entries.GroupBy(e =>
        {
            int slash = e.Path.IndexOf('/');
            return slash == -1 ? (string?)null : e.Path[..slash];
        });

        foreach (var group in grouped)
        {
            if (group.Key is null)
            {
                foreach (var entry in group)
                    tree.Entries.Add(new GitTreeEntry { Mode = "100644", Name = entry.Path, Sha = entry.Sha });
            }
            else
            {
                var subEntries = group.Select(e => new FileEntry(e.Path[(group.Key.Length + 1)..], e.Sha));
                string subTreeSha = BuildTreeFromIndex(subEntries);
                tree.Entries.Add(new GitTreeEntry { Mode = "40000", Name = group.Key, Sha = subTreeSha });
            }
        }

        tree.Write(ObjectsDir);
        return tree.ComputeHash();
    }

    private string BuildTreeFromEntries(GitIndex index) =>
        BuildTreeFromIndex(index.Entries.Select(e =>
            new FileEntry(e.Path, Convert.ToHexString(e.Sha).ToLower())));
}
