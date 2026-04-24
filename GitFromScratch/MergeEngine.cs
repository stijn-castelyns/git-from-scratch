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
}
