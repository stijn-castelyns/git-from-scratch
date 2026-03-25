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
    /// Creates a new branch pointing at the current HEAD commit.
    /// </summary>
    public void CreateBranch(string branchName)
    {
        string? commitSha = ResolveHead();
        if (commitSha is null)
            throw new InvalidOperationException("fatal: not a valid object name: 'HEAD'");

        string refPath = Path.Combine(_gitDir, "refs", "heads", branchName.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(refPath))
            throw new InvalidOperationException($"fatal: a branch named '{branchName}' already exists");

        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commitSha + "\n");
    }

    /// <summary>
    /// Points HEAD at a different branch (symref update).
    /// </summary>
    public void SetHead(string branchName)
    {
        File.WriteAllText(Path.Combine(_gitDir, "HEAD"), $"ref: refs/heads/{branchName}\n");
    }

    /// <summary>
    /// Returns true if the branch ref file exists.
    /// </summary>
    public bool BranchExists(string branchName)
    {
        string refPath = Path.Combine(_gitDir, "refs", "heads", branchName.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath);
    }

    /// <summary>
    /// Resolves a branch name to a commit SHA.
    /// </summary>
    public string? ResolveBranch(string branchName)
    {
        string refPath = Path.Combine(_gitDir, "refs", "heads", branchName.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
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
