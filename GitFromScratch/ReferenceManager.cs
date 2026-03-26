namespace GitFromScratch;

internal class ReferenceManager
{
    private readonly string _gitDir;

    public ReferenceManager(string gitDir) => _gitDir = gitDir;

    public string? ResolveHead()
    {
        var (isSymref, target) = ParseHead();
        if (!isSymref) return target;
        string refPath = Path.Combine(_gitDir, target.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
    }

    public void UpdateHead(string commitSha)
    {
        var (isSymref, target) = ParseHead();
        if (!isSymref)
            throw new InvalidOperationException("HEAD is detached; cannot update ref.");

        string refPath = Path.Combine(_gitDir, target.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    public void CreateBranch(string branchName)
    {
        string? commitSha = ResolveHead()
            ?? throw new InvalidOperationException("fatal: not a valid object name: 'HEAD'");

        string refPath = BranchRefPath(branchName);
        if (File.Exists(refPath))
            throw new InvalidOperationException($"fatal: a branch named '{branchName}' already exists");

        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    public void SetHead(string branchName) =>
        File.WriteAllText(Path.Combine(_gitDir, "HEAD"), $"ref: refs/heads/{branchName}\n");

    public bool BranchExists(string branchName) => File.Exists(BranchRefPath(branchName));

    public string? ResolveBranch(string branchName)
    {
        string refPath = BranchRefPath(branchName);
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
    }

    public string GetCurrentBranch()
    {
        var (isSymref, target) = ParseHead();
        if (isSymref && target.StartsWith("refs/heads/"))
            return target["refs/heads/".Length..];
        throw new InvalidOperationException("HEAD is detached.");
    }

    private (bool isSymref, string target) ParseHead()
    {
        string content = File.ReadAllText(Path.Combine(_gitDir, "HEAD")).Trim();
        return content.StartsWith("ref: ") ? (true, content[5..]) : (false, content);
    }

    private string BranchRefPath(string name) =>
        Path.Combine(_gitDir, "refs", "heads", name.Replace('/', Path.DirectorySeparatorChar));
}
