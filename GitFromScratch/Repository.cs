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

        string treeSha = GitTree.FromIndex(index, ObjectsDir).ComputeHash();
        ReferenceManager refs = new ReferenceManager(GitDir);
        string? parentSha = refs.ResolveHead();

        List<string> parents = parentSha is not null ? [parentSha] : [];

        // Abort if tree is identical to parent's tree (nothing changed)
        if (parentSha is not null)
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

        return commit.Sha;
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

        throw new NotImplementedException("TO-DO: Implement merge logic for non-fast-forward cases");
    }
}
