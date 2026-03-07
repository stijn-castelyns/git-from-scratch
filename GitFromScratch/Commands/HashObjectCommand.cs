using GitFromScratch.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace GitFromScratch.Commands;

public class HashObjectCommand : Command<HashObjectCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        public string FilePath { get; set; }
        [CommandOption("-w|--write")]
        [DefaultValue(false)]
        public FlagValue<bool> WriteObject { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Repository repo = Repository.Open(settings.FilePath);

        GitObject gitObject = repo.HashObject(settings.FilePath, write: settings.WriteObject.Value);

        AnsiConsole.WriteLine(gitObject.Sha);

        return 0;
    }
}
