using GitFromScratch.Models;
using System;
using System.Collections.Generic;
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

        while(dir is not null)
        {
            if(Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return new Repository(dir.FullName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("fatal: not a git repository (or any parent directories): .git");
    }

    public GitObject HashObject(string filePath, string type = "blob", bool write = false)
    {
        byte[]? fileContent = File.ReadAllBytes(filePath);

        GitObject gitObject = type switch
        {
            "blob" => new GitBlob(fileContent),
            _ => throw new InvalidOperationException()
        };

        if (write)
        {
            gitObject.Write(Path.Combine(GitDir, ".objects"));
        }

        return gitObject;
    }
}
