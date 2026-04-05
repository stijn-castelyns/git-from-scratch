using GitFromScratch.Models;

namespace GitFromScratch;

internal enum MergeResult
{
    FastForward
}

internal class MergeEngine
{
    private readonly string _gitDir;
    private readonly string _objectsDir;
    private readonly WorkingTree _workingTree;

    public MergeEngine(string gitDir, string objectsDir, WorkingTree workingTree)
    {
        _gitDir = gitDir;
        _objectsDir = objectsDir;
        _workingTree = workingTree;
    }

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
            _workingTree.CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (oursSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        string? baseSha = FindMergeBase(oursSha, theirsSha);

        if (baseSha == oursSha)
        {
            refs.UpdateHead(theirsSha);
            _workingTree.CheckoutTree(theirsSha);
            return MergeResult.FastForward;
        }

        if (baseSha == theirsSha)
            throw new InvalidOperationException("Already up to date.");

        return null;
    }

    public string? FindMergeBase(string sha1, string sha2)
    {
        HashSet<string> ancestors1 = [sha1];
        HashSet<string> ancestors2 = [sha2];
        Queue<string> queue1 = new([sha1]);
        Queue<string> queue2 = new([sha2]);

        while (queue1.Count > 0 || queue2.Count > 0)
        {
            if (Step(queue1, ancestors1, ancestors2, out string? found)) return found;
            if (Step(queue2, ancestors2, ancestors1, out found)) return found;
        }

        return null;

        bool Step(Queue<string> queue, HashSet<string> visited, HashSet<string> other, out string? match)
        {
            match = null;
            if (queue.Count == 0) return false;

            string current = queue.Dequeue();
            if (other.Contains(current)) { match = current; return true; }

            if (GitObject.Read(current, _objectsDir) is GitCommit commit)
                foreach (string parent in commit.Parents)
                    if (visited.Add(parent))
                        queue.Enqueue(parent);

            return false;
        }
    }
}
