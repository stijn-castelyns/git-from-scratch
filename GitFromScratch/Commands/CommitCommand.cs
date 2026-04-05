using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class CommitCommand : Command<CommitCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-m|--message")]
        public string? Message { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Message))
        {
            AnsiConsole.MarkupLine("[red]error: commit message required (-m)[/]");
            return 1;
        }

        Repository repo = Repository.Open(".");
        string sha = repo.Commit(settings.Message);

        ReferenceManager refs = new ReferenceManager(repo.GitDir);
        string branch = refs.GetCurrentBranch();

        AnsiConsole.MarkupLine($"[green][[{branch} {sha[..7]}]][/] {settings.Message}");
        return 0;
    }
}
