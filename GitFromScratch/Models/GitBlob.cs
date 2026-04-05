namespace GitFromScratch.Models;

public class GitBlob : GitObject
{
    public override string Type => "blob";
    public byte[] Data { get; }

    public GitBlob(byte[] data)
    {
        Data = data;
        ComputeHash();
    }

    public override byte[] Serialize() => Data;
}