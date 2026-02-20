namespace PrStats.Models;

public sealed class OutlierPrResult
{
    public required PullRequestMetrics Metrics { get; init; }
    public required double CompositeScore { get; init; }
    public required List<OutlierFlag> Flags { get; init; }
}

public sealed class OutlierFlag
{
    public required string Label { get; init; }
    public required string CssClass { get; init; }
    public required double ZScore { get; init; }
}
