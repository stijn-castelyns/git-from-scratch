using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class AddCommand : Command<AddCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        public string? FilePath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath!);
        repo.Add(settings.FilePath!);
        return 0;
    }
}
