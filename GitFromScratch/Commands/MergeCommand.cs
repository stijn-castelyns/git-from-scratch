using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class MergeCommand : Command<MergeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<branch>")]
        public string Branch { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");

        MergeResult result = repo.Merge(settings.Branch);

        switch (result)
        {
            case MergeResult.FastForward:
                AnsiConsole.MarkupLine($"[green]Fast-forward[/]");
                break;
            case MergeResult.Merged:
                AnsiConsole.MarkupLine($"[green]Merge made by the 'ort' strategy.[/]");
                break;
            case MergeResult.Conflict:
                AnsiConsole.MarkupLine($"[yellow]Auto-merging failed; fix conflicts and then commit the result.[/]");
                return 1;
        }

        return 0;
    }
}
