namespace PrStats.Models;

public sealed class TeamComparisonEntry
{
    public required string Label { get; init; }
    public required PrStatsReport Report { get; init; }
    public required PercentileMetrics Percentiles { get; init; }
    public required double PrsPerWeek { get; init; }
    public required int UniqueContributorCount { get; init; }
}

public sealed class PercentileMetrics
{
    public TimeSpan? MedianCycleTime { get; init; }
    public TimeSpan? P75CycleTime { get; init; }
    public TimeSpan? MedianTimeToFirstComment { get; init; }
    public TimeSpan? P75TimeToFirstComment { get; init; }
    public TimeSpan? MedianTimeToFirstApproval { get; init; }
    public TimeSpan? P75TimeToFirstApproval { get; init; }
}

public enum BenchmarkTier
{
    Elite,
    Good,
    Fair,
    NeedsFocus,
}

public static class IndustryBenchmarks
{
    // Cycle Time thresholds (LinearB)
    public static readonly TimeSpan CycleTimeElite = TimeSpan.FromHours(26);
    public static readonly TimeSpan CycleTimeGood = TimeSpan.FromHours(80);
    public static readonly TimeSpan CycleTimeFair = TimeSpan.FromHours(167);

    // First Review Time thresholds (LinearB)
    public static readonly TimeSpan FirstReviewElite = TimeSpan.FromMinutes(75);
    public static readonly TimeSpan FirstReviewGood = TimeSpan.FromHours(4);
    public static readonly TimeSpan FirstReviewFair = TimeSpan.FromHours(12);

    public static BenchmarkTier ClassifyCycleTime(TimeSpan value)
    {
        if (value <= CycleTimeElite) return BenchmarkTier.Elite;
        if (value <= CycleTimeGood) return BenchmarkTier.Good;
        if (value <= CycleTimeFair) return BenchmarkTier.Fair;
        return BenchmarkTier.NeedsFocus;
    }

    public static BenchmarkTier ClassifyFirstReviewTime(TimeSpan value)
    {
        if (value <= FirstReviewElite) return BenchmarkTier.Elite;
        if (value <= FirstReviewGood) return BenchmarkTier.Good;
        if (value <= FirstReviewFair) return BenchmarkTier.Fair;
        return BenchmarkTier.NeedsFocus;
    }

    public static string TierLabel(BenchmarkTier tier) => tier switch
    {
        BenchmarkTier.Elite => "Elite",
        BenchmarkTier.Good => "Good",
        BenchmarkTier.Fair => "Fair",
        BenchmarkTier.NeedsFocus => "Needs Focus",
        _ => "Unknown",
    };

    public static string TierColor(BenchmarkTier tier) => tier switch
    {
        BenchmarkTier.Elite => "#10b981",
        BenchmarkTier.Good => "#3b82f6",
        BenchmarkTier.Fair => "#f59e0b",
        BenchmarkTier.NeedsFocus => "#ef4444",
        _ => "#94a3b8",
    };
}
