using System.Text;

namespace GitFromScratch.Models;

public class GitCommit : GitObject
{
    public override string Type => "commit";
    public string TreeSha { get; }
    public List<string> Parents { get; } = new();
    public string Author { get; }
    public string Committer { get; }
    public string Message { get; }

    private readonly long _timestamp;
    private readonly byte[] _rawContent;

    public GitCommit(string treeSha, IEnumerable<string> parents, string author, string committer, string message)
    {
        TreeSha = treeSha;
        Parents = parents.ToList();
        Author = author;
        Committer = committer;
        Message = message;
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _rawContent = BuildContent();
    }

    /// <summary>
    /// Constructs a GitCommit by parsing raw content bytes (used by GitObject.Read).
    /// </summary>
    public GitCommit(byte[] content)
    {
        _rawContent = content;
        string[] parts = Encoding.UTF8.GetString(content).Split("\n\n", 2);
        Message = parts.Length > 1 ? parts[1].TrimEnd('\n') : "";

        foreach (string line in parts[0].Split('\n'))
        {
            if (line.StartsWith("tree ")) TreeSha = line[5..];
            else if (line.StartsWith("parent ")) Parents.Add(line[7..]);
            else if (line.StartsWith("author ")) Author = line[7..];
            else if (line.StartsWith("committer ")) Committer = line[10..];
        }

        TreeSha ??= "";
        Author ??= "";
        Committer ??= "";
    }

    public override byte[] Serialize() => _rawContent;

    private byte[] BuildContent()
    {
        string parents = string.Concat(Parents.Select(p => $"parent {p}\n"));
        return Encoding.UTF8.GetBytes(
            $"tree {TreeSha}\n{parents}" +
            $"author {Author} {_timestamp} +0000\n" +
            $"committer {Committer} {_timestamp} +0000\n" +
            $"\n{Message}\n");
    }
}
