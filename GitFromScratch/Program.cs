using GitFromScratch.Commands;
using Spectre.Console.Cli;

CommandApp? app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<InitRepoCommand>("init");
});

return app.Run(args);