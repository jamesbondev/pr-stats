using PrStats.Configuration;
using Spectre.Console.Cli;

var app = new CommandApp();
app.SetDefaultCommand<PrStatsCommand>();
app.Configure(config =>
{
    config.SetApplicationName("prstats");
    config.SetApplicationVersion("1.0.0");
    config.AddCommand<PrStatsCommand>("report");
    config.AddCommand<ChatCommand>("chat");
    config.AddCommand<CompareCommand>("compare");
});

return await app.RunAsync(args);
