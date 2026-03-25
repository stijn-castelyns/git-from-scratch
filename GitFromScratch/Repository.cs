using GitFromScratch.Models;
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch;

internal class Repository
{
    public string WorkDir { get; }
    public string GitDir { get; set; }

    private Repository(string workDir)
    {
        WorkDir = Path.GetFullPath(workDir);
        GitDir = Path.Combine(workDir, ".git");
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
        byte[] fileContent = File.ReadAllBytes(filePath);

        // Git normalizes line endings (CRLF → LF) before hashing
        fileContent = NormalizeLineEndings(fileContent);

        GitObject gitObject = type switch
        {
            "blob" => new GitBlob(fileContent),
            _ => throw new InvalidOperationException()
        };

        if (write)
        {
            gitObject.Write(Path.Combine(GitDir, "objects"));
        }

        return gitObject;
    }

    public void Add(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string relativePath = Path.GetRelativePath(WorkDir, fullPath).Replace('\\', '/');

        GitBlob blob = (GitBlob)HashObject(fullPath, write: true);

        GitIndex index = new GitIndex(GitDir);
        index.Add(relativePath, blob, new FileInfo(fullPath));
    }

    private static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }
}
