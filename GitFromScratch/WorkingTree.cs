using GitFromScratch.Models;
using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch;

internal class WorkingTree
{
    private readonly string _workDir;
    private readonly string _gitDir;
    private readonly string _objectsDir;

    public WorkingTree(string workDir, string gitDir, string objectsDir)
    {
        _workDir = workDir;
        _gitDir = gitDir;
        _objectsDir = objectsDir;
    }

    public static byte[] NormalizeLineEndings(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        text = text.Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }

    public void DeleteFromWorkTree(string path)
    {
        string filePath = ToWorkTreePath(path);
        if (File.Exists(filePath))
            File.Delete(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir != _workDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public void AddToIndexAndWorkTree(GitIndex index, string path, string blobSha)
    {
        if (GitObject.Read(blobSha, _objectsDir) is not GitBlob blob)
            throw new InvalidOperationException($"fatal: expected blob for {path}");

        string filePath = ToWorkTreePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, blob.Data);
        index.Add(path, blob, new FileInfo(filePath));
    }

    public void CheckoutTree(string commitSha)
    {
        GitIndex currentIndex = new GitIndex(_gitDir);
        foreach (GitIndexEntry entry in currentIndex.Entries)
            DeleteFromWorkTree(entry.Path);

        if (GitObject.Read(commitSha, _objectsDir) is not GitCommit targetCommit)
            throw new InvalidOperationException("fatal: not a commit object");

        GitIndex newIndex = new GitIndex(_gitDir);
        newIndex.Entries.Clear();

        foreach (GitTreeEntry treeEntry in GitTree.Flatten(targetCommit.TreeSha, _objectsDir))
            AddToIndexAndWorkTree(newIndex, treeEntry.Name, treeEntry.Sha);

        newIndex.SortEntries();
        newIndex.Save();
    }

    public string ToWorkTreePath(string gitPath) =>
        Path.Combine(_workDir, gitPath.Replace('/', Path.DirectorySeparatorChar));
}
