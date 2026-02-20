namespace PrStats.Models;

public sealed class PrStatsReport
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required string Organization { get; init; }
    public required string Project { get; init; }
    public required string RepositoryDisplayName { get; init; }
    public required int Days { get; init; }
    public required List<PullRequestData> PullRequests { get; init; }
    public required List<PullRequestMetrics> Metrics { get; init; }
    public required TeamMetricsSummary TeamMetrics { get; init; }
}

public sealed class TeamMetricsSummary
{
    // Counts
    public required int TotalPrCount { get; init; }
    public required int CompletedPrCount { get; init; }
    public required int AbandonedPrCount { get; init; }
    public required int ActivePrCount { get; init; }

    // Cycle time aggregates
    public TimeSpan? AvgCycleTime { get; init; }
    public TimeSpan? MedianCycleTime { get; init; }
    public TimeSpan? AvgTimeToFirstComment { get; init; }
    public TimeSpan? AvgTimeToFirstApproval { get; init; }

    // Size aggregates
    public double AvgFilesChanged { get; init; }
    public double AvgCommitsPerPr { get; init; }

    // Quality rates
    public double AbandonedRate { get; init; }
    public double FirstTimeApprovalRate { get; init; }
    public double ThreadResolutionRate { get; init; }

    // Throughput
    public required Dictionary<string, List<WeeklyCount>> ThroughputByAuthor { get; init; }

    // Review load balance
    public required Dictionary<string, int> ReviewsPerPerson { get; init; }

    // Comment threads initiated on others' PRs
    public required Dictionary<string, int> CommentsPerPerson { get; init; }

    // Top creators
    public required Dictionary<string, int> PrsPerAuthor { get; init; }

    // Pairing (converted from Dictionary<ReviewerAuthorPair, int>)
    public required List<PairingEntry> PairingMatrix { get; init; }

    // Per-repo breakdown
    public required Dictionary<string, RepositoryBreakdown> PerRepositoryBreakdown { get; init; }
}
