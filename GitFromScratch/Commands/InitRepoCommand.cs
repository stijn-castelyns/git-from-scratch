using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class InitRepoCommand : Command<InitRepoCommand.Settings>
{

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        public string Path { get; set; } = ".";
    }
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Init(settings.Path);
        AnsiConsole.MarkupLine($"Initialized empty repository in [blue]{repo.GitDir}[/]");

        return 0;
    }
}
