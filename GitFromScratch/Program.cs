using GitFromScratch.Commands;
using Spectre.Console.Cli;

CommandApp? app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<InitRepoCommand>("init");
    config.AddCommand<HashObjectCommand>("hash-object");
    config.AddCommand<AddCommand>("add");
});

return app.Run(args);