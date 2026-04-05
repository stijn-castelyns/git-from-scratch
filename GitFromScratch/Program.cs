using Spectre.Console.Cli;

CommandApp? app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<HelloWorldCommand>("hello");
});

return app.Run(args);

public class HelloWorldCommand : Command<HelloWorldCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; set; } = string.Empty;
    }
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello, {settings.Name}!");
        return 0;
    }
}