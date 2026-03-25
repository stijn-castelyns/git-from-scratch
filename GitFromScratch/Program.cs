using GitFromScratch.Commands;
using Spectre.Console.Cli;

CommandApp? app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<InitRepoCommand>("init");
    config.AddCommand<HashObjectCommand>("hash-object");
    config.AddCommand<AddCommand>("add");
    config.AddCommand<CommitCommand>("commit");
    config.AddCommand<BranchCommand>("branch");
    config.AddCommand<CheckoutCommand>("checkout");
    config.AddCommand<MergeCommand>("merge");
});

return app.Run(args);