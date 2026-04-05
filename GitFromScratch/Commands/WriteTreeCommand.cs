using GitFromScratch.Models;
using GitFromScratch.Staging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class WriteTreeCommand : Command<WriteTreeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        public string? FilePath { get; set; } = ".";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath);

        GitIndex index = new GitIndex(repo.GitDir);

        GitTree tree = GitTree.FromIndex(index, repo.ObjectsDir);

        tree.Write(repo.ObjectsDir);

        AnsiConsole.WriteLine(tree.Sha);

        return 0;
    }
}
