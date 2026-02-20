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
    public double SelfMergedRate { get; init; }
    public double UnreviewedRate { get; init; }
    public double ThreadResolutionRate { get; init; }

    // Throughput: PRs merged per week, per author
    public required Dictionary<string, List<WeeklyCount>> ThroughputByAuthor { get; init; }

    // Review load balance
    public required Dictionary<string, int> ReviewsPerPerson { get; init; }

    // Top creators
    public required Dictionary<string, int> PrsPerAuthor { get; init; }

    // Reviewer-Author pairing matrix
    public required Dictionary<ReviewerAuthorPair, int> PairingMatrix { get; init; }
}

public sealed class WeeklyCount
{
    public required DateTime WeekStart { get; init; }
    public required int Count { get; init; }
}
