namespace PrStats.Models;

public sealed class BuildInfo
{
    public required int BuildId { get; init; }
    public required string DefinitionName { get; init; }
    public required int DefinitionId { get; init; }
    public required string Status { get; init; }
    public string? Result { get; init; }
    public required DateTime QueueTime { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? FinishTime { get; init; }
    public string? SourceVersion { get; init; }
}

public sealed class PrBuildMetrics
{
    public required int TotalBuildCount { get; init; }
    public required int SucceededCount { get; init; }
    public required int FailedCount { get; init; }
    public required int CanceledCount { get; init; }
    public required int PartiallySucceededCount { get; init; }
    public required double BuildSuccessRate { get; init; }
    public TimeSpan? AvgQueueTime { get; init; }
    public TimeSpan? AvgRunTime { get; init; }
    public TimeSpan? TotalElapsedTime { get; init; }
    public TimeSpan? TotalRunTime { get; init; }
    public required List<PipelineSummary> PerPipeline { get; init; }
}

public sealed class PipelineSummary
{
    public required string DefinitionName { get; init; }
    public required int RunCount { get; init; }
    public required int SucceededCount { get; init; }
    public required int FailedCount { get; init; }
    public TimeSpan? AvgDuration { get; init; }
}

public sealed class TeamBuildMetrics
{
    public required int TotalBuildsAcrossAllPrs { get; init; }
    public required double AvgBuildsPerPr { get; init; }
    public required double MedianBuildsPerPr { get; init; }
    public required double OverallBuildSuccessRate { get; init; }
    public TimeSpan? AvgBuildRunTime { get; init; }
    public TimeSpan? MedianBuildRunTime { get; init; }
    public TimeSpan? AvgQueueTime { get; init; }
    public TimeSpan? AvgCiElapsedTimePerPr { get; init; }
    public TimeSpan? TotalCiElapsedTime { get; init; }
    public required Dictionary<string, PipelineTeamSummary> PerPipeline { get; init; }
}

public sealed class PipelineTeamSummary
{
    public required int TotalRuns { get; init; }
    public required double SuccessRate { get; init; }
    public TimeSpan? AvgDuration { get; init; }
}
