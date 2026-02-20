namespace PrStats.Models;

public sealed class TeamMetrics
{
    public required int TotalPrCount { get; init; }
    public required int CompletedPrCount { get; init; }
    public required int AbandonedPrCount { get; init; }
    public required int ActivePrCount { get; init; }

    // Cycle time aggregates (completed, non-draft PRs only)
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

    // Throughput: PRs merged per week, per author
    public required Dictionary<string, List<WeeklyCount>> ThroughputByAuthor { get; init; }

    // Review load balance
    public required Dictionary<string, int> ReviewsPerPerson { get; init; }

    // Comment threads initiated on others' PRs
    public required Dictionary<string, int> CommentsPerPerson { get; init; }

    // Top creators
    public required Dictionary<string, int> PrsPerAuthor { get; init; }

    // Reviewer-Author pairing matrix
    public required Dictionary<ReviewerAuthorPair, int> PairingMatrix { get; init; }

    // Per-repository breakdown (populated when multiple repos)
    public required Dictionary<string, RepositoryBreakdown> PerRepositoryBreakdown { get; init; }
}

public sealed class RepositoryBreakdown
{
    public required int TotalPrCount { get; init; }
    public required int CompletedPrCount { get; init; }
    public required int AbandonedPrCount { get; init; }
    public required int ActivePrCount { get; init; }
    public double AbandonedRate { get; init; }
    public TimeSpan? AvgCycleTime { get; init; }
    public TimeSpan? MedianCycleTime { get; init; }
    public double AvgFilesChanged { get; init; }
    public double FirstTimeApprovalRate { get; init; }
}

public sealed class WeeklyCount
{
    public required DateTime WeekStart { get; init; }
    public required int Count { get; init; }
}
