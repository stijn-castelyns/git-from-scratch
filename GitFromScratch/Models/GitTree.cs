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
    public override byte[] Serialize()
    {
        IOrderedEnumerable<GitTreeEntry> sorted = Entries.OrderBy(e => e.Name, StringComparer.Ordinal);
        using MemoryStream? ms = new MemoryStream();

        foreach (GitTreeEntry treeEntry in sorted)
        {
            var header = Encoding.ASCII.GetBytes($"{treeEntry.Mode} {treeEntry.Name}\0");
            var sha = Convert.FromHexString(treeEntry.Sha);
            ms.Write(header);
            ms.Write(sha);
        }

        return ms.ToArray();
    }
}
