using GitFromScratch.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GitFromScratch.Commands;

public class HashObjectCommand : Command<HashObjectCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        public string? FilePath { get; set; }
        [CommandOption("-w|--write")]
        public bool WriteObject { get; set; } = false;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath);

        GitObject gitObject = repo.HashObject(settings.FilePath, write: settings.WriteObject);

        AnsiConsole.WriteLine(gitObject.Sha);

        return 0;
    }
}
