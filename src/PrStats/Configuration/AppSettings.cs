namespace PrStats.Configuration;

public sealed record AppSettings
{
    public required string Organization { get; init; }
    public required string Project { get; init; }
    public List<string> Repositories { get; init; } = [];
    public required string Pat { get; init; }
    public int Days { get; init; } = 90;
    public string Output { get; init; } = "pr-report.html";
    public List<string> BotNames { get; init; } = [];
    public List<string> BotIds { get; init; } = [];
    public bool NoOpen { get; init; }
    public int? MaxPrs { get; init; }
    public bool NoCache { get; init; }
    public bool ClearCache { get; init; }
    public List<string> Authors { get; init; } = [];
    public List<string> AuthorIds { get; init; } = [];
    public bool Json { get; init; }

    public bool HasAuthorFilter => Authors.Count > 0 || AuthorIds.Count > 0;

    public bool AllRepositories => Repositories.Count == 0;

    public string RepositoryDisplayName => Repositories.Count switch
    {
        0 => "All Repositories",
        1 => Repositories[0],
        _ => string.Join(", ", Repositories),
    };
}
