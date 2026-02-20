namespace PrStats.Models;

public sealed class PullRequestData
{
    public required int PullRequestId { get; init; }
    public required string Title { get; init; }
    public required string RepositoryName { get; init; }
    public required PrStatus Status { get; init; }
    public required bool IsDraft { get; init; }
    public required DateTime CreationDate { get; init; }
    public DateTime? ClosedDate { get; init; }
    public DateTime? PublishedDate { get; init; }

    public required string AuthorDisplayName { get; init; }
    public required string AuthorId { get; init; }
    public bool IsAuthorBot { get; init; }

    public string? ClosedByDisplayName { get; init; }
    public string? ClosedById { get; init; }

    public required List<ReviewerInfo> Reviewers { get; init; }
    public required List<ThreadInfo> Threads { get; init; }
    public required List<IterationInfo> Iterations { get; init; }

    public int FilesChanged { get; init; }
    public int CommitCount { get; init; }
}

public enum PrStatus
{
    Active,
    Completed,
    Abandoned,
}

public sealed class ReviewerInfo
{
    public required string DisplayName { get; init; }
    public required string Id { get; init; }
    public required int Vote { get; init; }
    public required bool IsContainer { get; init; }
    public required bool IsRequired { get; init; }
}

public sealed class ThreadInfo
{
    public required int ThreadId { get; init; }
    public required string CommentType { get; init; }
    public required DateTime PublishedDate { get; init; }
    public required string AuthorDisplayName { get; init; }
    public required string AuthorId { get; init; }
    public required bool IsAuthorBot { get; init; }
    public required string Status { get; init; }
    public required int CommentCount { get; init; }
    public required bool IsVoteUpdate { get; init; }
    public int? VoteValue { get; init; }
}

public sealed class IterationInfo
{
    public required int IterationId { get; init; }
    public required DateTime CreatedDate { get; init; }
    public required string Reason { get; init; }
}
