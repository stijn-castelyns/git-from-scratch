using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class BranchCommand : Command<BranchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");
        ReferenceManager refs = new ReferenceManager(repo.GitDir);

        refs.CreateBranch(settings.Name);

        AnsiConsole.MarkupLine($"[green]Created branch '{settings.Name}'[/]");
        return 0;
    }
}
