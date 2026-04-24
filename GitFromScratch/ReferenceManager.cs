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

    public void CreateBranch(string branchName)
    {
        string commitSha = ResolveHead()
            ?? throw new InvalidOperationException("fatal: not a valid object name: 'HEAD'");

        if (BranchExists(branchName))
            throw new InvalidOperationException($"fatal: a branch named '{branchName}' already exists");

        WriteRef($"refs/heads/{branchName}", commitSha);
    }

    public bool BranchExists(string branchName) => File.Exists(RefPath($"refs/heads/{branchName}"));

    public string? ResolveBranch(string branchName) => ReadRef($"refs/heads/{branchName}");

    public void SetHead(string branchName) =>
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/{branchName}\n");

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
