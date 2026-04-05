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
        }

        return 0;
    }
}
