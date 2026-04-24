using GitFromScratch.Models;
using GitFromScratch.Staging;

namespace GitFromScratch;

internal class Repository
{
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

    public GitBlob HashObject(string filePath, bool write = false)
    {
        byte[] fileContent = WorkingTree.NormalizeLineEndings(File.ReadAllBytes(filePath));

        GitBlob gitBlob = new GitBlob(fileContent);

        if (write) gitBlob.Write(ObjectsDir);
        return gitBlob;
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
            GitBlob blob = HashObject(file, write: true);
            index.Add(relativePath, blob, new FileInfo(file));
        }

        index.SortEntries();
        index.Save();
    }

    public string Commit(string message)
    {
        GitIndex index = new GitIndex(GitDir);
        if (index.Entries.Count == 0)
            throw new InvalidOperationException("nothing to commit");

        string treeSha = GitTree.FromIndex(index, ObjectsDir).Sha;
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

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

    public MergeResult Merge(string branchName)
    {
        ReferenceManager refs = new ReferenceManager(GitDir);
        MergeEngine merger = new MergeEngine(ObjectsDir, _workingTree);

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
}
