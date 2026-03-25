namespace GitFromScratch;

internal class ReferenceManager
{
    private readonly string _gitDir;

    public ReferenceManager(string gitDir)
    {
        _gitDir = gitDir;
    }

    /// <summary>
    /// Resolves HEAD to a commit SHA. Returns null if the branch has no commits yet.
    /// </summary>
    public string? ResolveHead()
    {
        string headContent = File.ReadAllText(Path.Combine(_gitDir, "HEAD")).Trim();

        if (headContent.StartsWith("ref: "))
        {
            string refPath = Path.Combine(_gitDir, headContent[5..].Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
        }

        // Detached HEAD — raw SHA
        return headContent;
    }

    /// <summary>
    /// Updates the ref that HEAD points to with the given commit SHA.
    /// </summary>
    public void UpdateHead(string commitSha)
    {
        string headContent = File.ReadAllText(Path.Combine(_gitDir, "HEAD")).Trim();

        if (!headContent.StartsWith("ref: "))
            throw new InvalidOperationException("HEAD is detached; cannot update ref.");

        string refPath = Path.Combine(_gitDir, headContent[5..].Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    /// <summary>
    /// Returns the current branch name (e.g., "main").
    /// </summary>
    public string GetCurrentBranch()
    {
        string headContent = File.ReadAllText(Path.Combine(_gitDir, "HEAD")).Trim();

        if (headContent.StartsWith("ref: refs/heads/"))
            return headContent["ref: refs/heads/".Length..];

        throw new InvalidOperationException("HEAD is detached.");
    }
}
