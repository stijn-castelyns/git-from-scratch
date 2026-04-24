using GitFromScratch.Staging;
using System.Text;

namespace GitFromScratch.Models;

public record GitTreeEntry(string Mode, string Name, string Sha);

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
            int spaceIdx = Array.IndexOf(content, (byte)' ', i);
            string mode = Encoding.ASCII.GetString(content, i, spaceIdx - i);

            int nullIdx = Array.IndexOf(content, (byte)0, spaceIdx + 1);
            string name = Encoding.ASCII.GetString(content, spaceIdx + 1, nullIdx - spaceIdx - 1);

            byte[] shaBytes = content[(nullIdx + 1)..(nullIdx + 21)];
            string sha = Convert.ToHexString(shaBytes).ToLower();

            Entries.Add(new GitTreeEntry(mode, name, sha));
            i = nullIdx + 21;
        }
    }

    public static GitTree FromIndex(GitIndex index, string objectsDir) =>
        FromEntries(index.Entries.Select(e => (e.Path, Convert.ToHexString(e.Sha).ToLower())), objectsDir);

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
                    tree.Entries.Add(new GitTreeEntry("100644", entry.path, entry.sha));
            }
            else
            {
                IEnumerable<(string path, string sha)> subEntries = group.Select(e => (e.path[(group.Key.Length + 1)..], e.sha));
                GitTree subTree = FromEntries(subEntries, objectsDir);
                tree.Entries.Add(new GitTreeEntry("40000", group.Key, subTree.Sha));
            }
        }

        tree.Write(objectsDir);
        return tree;
    }

    public static IEnumerable<GitTreeEntry> Flatten(string treeSha, string objectsDir, string prefix = "")
    {
        if (GitObject.Read(treeSha, objectsDir) is not GitTree tree)
            throw new InvalidOperationException("fatal: expected tree object");

        foreach (GitTreeEntry entry in tree.Entries)
        {
            string fullPath = prefix == "" ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry.Mode == "40000")
                foreach (GitTreeEntry sub in Flatten(entry.Sha, objectsDir, fullPath))
                    yield return sub;
            else
                yield return entry with { Name = fullPath };
        }
    }

    public override byte[] Serialize()
    {
        IOrderedEnumerable<GitTreeEntry> sorted = Entries.OrderBy(e => e.Name, StringComparer.Ordinal);
        using MemoryStream ms = new MemoryStream();

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
