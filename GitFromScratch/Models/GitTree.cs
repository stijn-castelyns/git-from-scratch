using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch.Models;


public class GitTreeEntry
{
    public string Mode { get; set; }
    public string Name { get; set; }
    public string Sha { get; set; }
}

public class GitTree : GitObject
{
    public override string Type => "tree";
    public List<GitTreeEntry> Entries { get; } = new();

    public GitTree() { }

    /// <summary>
    /// Constructs a GitTree by parsing raw binary content (used by GitObject.Read).
    /// Format per entry: "<mode> <name>\0<20-byte-sha>"
    /// </summary>
    public GitTree(byte[] content)
    {
        int i = 0;
        while (i < content.Length)
        {
            // Find the space separating mode from name
            int spaceIdx = Array.IndexOf(content, (byte)' ', i);
            string mode = Encoding.ASCII.GetString(content, i, spaceIdx - i);

            // Find the null byte after the name
            int nullIdx = Array.IndexOf(content, (byte)0, spaceIdx + 1);
            string name = Encoding.ASCII.GetString(content, spaceIdx + 1, nullIdx - spaceIdx - 1);

            // Next 20 bytes are the raw SHA-1
            byte[] shaBytes = content[(nullIdx + 1)..(nullIdx + 21)];
            string sha = Convert.ToHexString(shaBytes).ToLower();

            Entries.Add(new GitTreeEntry { Mode = mode, Name = name, Sha = sha });
            i = nullIdx + 21;
        }
    }
    public static GitTree FromIndex(GitIndex index, string objectsDir)
    {
        IEnumerable<(string path, string sha)> entries = index.Entries
            .Select(e => (e.Path, Convert.ToHexString(e.Sha).ToLower()));
        return FromEntries(entries, objectsDir);
    }

    private static GitTree FromEntries(IEnumerable<(string path, string sha)> entries, string objectsDir)
    {
        GitTree tree = new GitTree();

        IEnumerable<IGrouping<string?, (string path, string sha)>> grouped = entries.GroupBy(e =>
        {
            int slash = e.path.IndexOf('/');
            return slash == -1 ? null : e.path[..slash];
        });

        foreach (IGrouping<string?, (string path, string sha)> group in grouped)
        {
            if (group.Key is null)
            {
                foreach ((string path, string sha) entry in group)
                    tree.Entries.Add(new GitTreeEntry { Mode = "100644", Name = entry.path, Sha = entry.sha });
            }
            else
            {
                IEnumerable<(string path, string sha)> subEntries = group.Select(e => (e.path[(group.Key.Length + 1)..], e.sha));
                GitTree subTree = FromEntries(subEntries, objectsDir);
                tree.Entries.Add(new GitTreeEntry { Mode = "40000", Name = group.Key, Sha = subTree.ComputeHash() });
            }
        }

        tree.Write(objectsDir);
        return tree;
    }

    public static List<GitTreeEntry> Flatten(string treeSha, string objectsDir)
    {
        List<GitTreeEntry> result = new();
        FlattenRecursive(treeSha, "", objectsDir, result);
        return result;
    }

    private static void FlattenRecursive(string treeSha, string prefix, string objectsDir, List<GitTreeEntry> result)
    {
        GitObject treeObj = GitObject.Read(treeSha, objectsDir);
        if (treeObj is not GitTree tree)
            throw new InvalidOperationException("fatal: expected tree object");

        foreach (GitTreeEntry entry in tree.Entries)
        {
            string fullPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry.Mode == "40000")
            {
                FlattenRecursive(entry.Sha, fullPath, objectsDir, result);
            }
            else
            {
                result.Add(new GitTreeEntry { Mode = entry.Mode, Name = fullPath, Sha = entry.Sha });
            }
        }
    }

    public override byte[] Serialize()
    {
        IOrderedEnumerable<GitTreeEntry> sorted = Entries.OrderBy(e => e.Name, StringComparer.Ordinal);
        using MemoryStream? ms = new MemoryStream();

        foreach (GitTreeEntry treeEntry in sorted)
        {
            byte[] header = Encoding.ASCII.GetBytes($"{treeEntry.Mode} {treeEntry.Name}\0");
            byte[] sha = Convert.FromHexString(treeEntry.Sha);
            ms.Write(header);
            ms.Write(sha);
        }

        return ms.ToArray();
    }
}
