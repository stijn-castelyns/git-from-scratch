namespace GitFromScratch;

internal class ReferenceManager
{
    private readonly string _gitDir;

    public ReferenceManager(string gitDir) => _gitDir = gitDir;

    public string? ResolveHead()
    {
        (bool isSymref, string? target) = ParseHead();
        if (!isSymref) return target;
        string refPath = Path.Combine(_gitDir, target.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
    }

    public void UpdateHead(string commitSha)
    {
        (bool isSymref, string? target) = ParseHead();
        if (!isSymref)
            throw new InvalidOperationException("HEAD is detached; cannot update ref.");

        string refPath = Path.Combine(_gitDir, target.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    public string GetCurrentBranch()
    {
        (bool isSymref, string? target) = ParseHead();
        if (isSymref && target.StartsWith("refs/heads/"))
            return target["refs/heads/".Length..];
        throw new InvalidOperationException("HEAD is detached.");
    }

    private (bool isSymref, string target) ParseHead()
    {
        string content = File.ReadAllText(Path.Combine(_gitDir, "HEAD")).Trim();
        return content.StartsWith("ref: ") ? (true, content[5..]) : (false, content);
    }
}
