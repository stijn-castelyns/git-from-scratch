using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class CheckoutCommand : Command<CheckoutCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<branch>")]
        public string Branch { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(".");

        repo.Checkout(settings.Branch);

        AnsiConsole.MarkupLine($"[green]Switched to branch '{settings.Branch}'[/]");
        return 0;
    }
}
