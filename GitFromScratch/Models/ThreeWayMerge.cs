namespace GitFromScratch.Models;

internal enum MergeAction { Keep, TakeOurs, TakeTheirs, Delete, Conflict }

internal record MergeFileResult(
    string Path,
    MergeAction Action,
    string? BlobSha,
    string? BaseSha,
    string? OursSha,
    string? TheirsSha);

internal static class ThreeWayMerge
{
    public static List<MergeFileResult> ResolveFiles(
        Dictionary<string, string> baseFiles,
        Dictionary<string, string> oursFiles,
        Dictionary<string, string> theirsFiles)
    {
        HashSet<string> allPaths = new(baseFiles.Keys);
        allPaths.UnionWith(oursFiles.Keys);
        allPaths.UnionWith(theirsFiles.Keys);

        List<MergeFileResult> results = new();

        foreach (string path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            baseFiles.TryGetValue(path, out string? baseSha);
            oursFiles.TryGetValue(path, out string? oursSha);
            theirsFiles.TryGetValue(path, out string? theirsSha);

            results.Add(Resolve(path, baseSha, oursSha, theirsSha));
        }

        return results;
    }

    private static MergeFileResult Resolve(string path, string? baseSha, string? oursSha, string? theirsSha)
    {
        bool oursChanged = oursSha != baseSha;
        bool theirsChanged = theirsSha != baseSha;
        bool bothAgree = oursSha == theirsSha;

        //   oursChanged | theirsChanged | bothAgree | Action
        //  ─────────────┼───────────────┼───────────┼────────────
        //        _      |       _       |   true    | Keep
        //     false     |     true      |   false   | TakeTheirs / Delete
        //     true      |     false     |   false   | TakeOurs   / Delete
        //     true      |     true      |   false   | Conflict

        (MergeAction action, string? blobSha) = (oursChanged, theirsChanged, bothAgree) switch
        {
            (_, _, true) => (MergeAction.Keep, oursSha),
            (false, true, false) => (theirsSha is not null ? MergeAction.TakeTheirs : MergeAction.Delete, theirsSha),
            (true, false, false) => (oursSha is not null ? MergeAction.TakeOurs : MergeAction.Delete, oursSha),
            (true, true, false) => (MergeAction.Conflict, (string?)null),
            // (false, false, false) is unreachable: if neither side changed, bothAgree is always true
        };

        return new MergeFileResult(path, action, blobSha, baseSha, oursSha, theirsSha);
    }
}
