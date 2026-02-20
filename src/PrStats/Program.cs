using PrStats.Configuration;
using Spectre.Console.Cli;

var app = new CommandApp<PrStatsCommand>();
app.Configure(config =>
{
    config.SetApplicationName("prstats");
    config.SetApplicationVersion("1.0.0");
});

return await app.RunAsync(args);
