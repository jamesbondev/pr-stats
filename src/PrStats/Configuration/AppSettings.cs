namespace PrStats.Configuration;

public sealed record AppSettings
{
    public required string Organization { get; init; }
    public required string Project { get; init; }
    public required string Repository { get; init; }
    public required string Pat { get; init; }
    public int Days { get; init; } = 90;
    public string Output { get; init; } = "pr-report.html";
    public List<string> BotNames { get; init; } = [];
    public List<string> BotIds { get; init; } = [];
    public bool NoOpen { get; init; }
}
