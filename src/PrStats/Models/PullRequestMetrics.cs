namespace PrStats.Models;

public sealed class PullRequestMetrics
{
    public required int PullRequestId { get; init; }
    public required string Title { get; init; }
    public required string RepositoryName { get; init; }
    public required PrStatus Status { get; init; }
    public required bool IsDraft { get; init; }
    public required string AuthorDisplayName { get; init; }
    public bool IsAuthorBot { get; init; }
    public required DateTime CreationDate { get; init; }
    public DateTime? ClosedDate { get; init; }
    public DateTime? PublishedDate { get; init; }

    // Cycle time metrics (null for active/draft PRs)
    public TimeSpan? TotalCycleTime { get; init; }
    public TimeSpan? TimeToFirstHumanComment { get; init; }
    public TimeSpan? TimeToFirstApproval { get; init; }
    public TimeSpan? TimeFromApprovalToMerge { get; init; }

    // Size metrics
    public int FilesChanged { get; init; }
    public int CommitCount { get; init; }
    public int IterationCount { get; init; }

    // Quality metrics
    public int HumanCommentCount { get; init; }
    public bool IsFirstTimeApproval { get; init; }
    public int ApprovalResetCount { get; init; }
    public int ResolvableThreadCount { get; init; }
    public int ResolvedThreadCount { get; init; }

    // Collaboration metrics
    public int ActiveReviewerCount { get; init; }
    public required List<string> ActiveReviewers { get; init; }

    // Pattern metrics
    public DayOfWeek CreationDayOfWeek { get; init; }
    public int CreationHourOfDay { get; init; }

    // For active PRs
    public TimeSpan? ActiveAge { get; init; }

    // Build metrics (null when --include-builds not used)
    public PrBuildMetrics? BuildMetrics { get; init; }
}
