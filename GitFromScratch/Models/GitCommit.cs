using System.Text;

namespace GitFromScratch.Models;

public class GitCommit : GitObject
{
    public override string Type => "commit";
    public string TreeSha { get; }
    public List<string> Parents { get; } = new();
    public string? ParentSha => Parents.Count > 0 ? Parents[0] : null;
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
        ComputeHash();
    }

    /// <summary>
    /// Constructs a GitCommit by parsing raw content bytes (used by GitObject.Read).
    /// </summary>
    public GitCommit(byte[] content)
    {
        _rawContent = content;
        string text = Encoding.UTF8.GetString(content);
        string[] lines = text.Split('\n');

        foreach (string line in lines)
        {
            if (line.StartsWith("tree "))
                TreeSha = line[5..];
            else if (line.StartsWith("parent "))
                Parents.Add(line[7..]);
            else if (line.StartsWith("author "))
                Author = line[7..];
            else if (line.StartsWith("committer "))
                Committer = line[10..];
            else if (line == "")
            {
                // Everything after the blank line is the message
                int msgStart = text.IndexOf("\n\n") + 2;
                Message = text[msgStart..].TrimEnd('\n');
                break;
            }
        }

        TreeSha ??= "";
        Author ??= "";
        Committer ??= "";
        Message ??= "";
    }

    public override byte[] Serialize() => _rawContent;

    private byte[] BuildContent()
    {
        StringBuilder sb = new();

        sb.Append($"tree {TreeSha}\n");

        foreach (string parent in Parents)
            sb.Append($"parent {parent}\n");

        sb.Append($"author {Author} {_timestamp} +0000\n");
        sb.Append($"committer {Committer} {_timestamp} +0000\n");
        sb.Append($"\n{Message}\n");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
