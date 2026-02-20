using PrStats.Models;

namespace PrStats.Services;

public static class ComparisonAnalyzer
{
    public static List<TeamComparisonEntry> Analyze(List<PrStatsReport> reports, List<string> labels)
    {
        var entries = new List<TeamComparisonEntry>();

        for (var i = 0; i < reports.Count; i++)
        {
            var report = reports[i];
            var label = labels[i];

            var completed = report.Metrics
                .Where(m => m.Status == PrStatus.Completed && !m.IsDraft && m.TotalCycleTime.HasValue)
                .ToList();

            var cycleTimes = completed
                .Select(m => m.TotalCycleTime!.Value)
                .ToList();

            var firstCommentTimes = completed
                .Where(m => m.TimeToFirstHumanComment.HasValue)
                .Select(m => m.TimeToFirstHumanComment!.Value)
                .ToList();

            var firstApprovalTimes = completed
                .Where(m => m.TimeToFirstApproval.HasValue)
                .Select(m => m.TimeToFirstApproval!.Value)
                .ToList();

            var percentiles = new PercentileMetrics
            {
                MedianCycleTime = Percentile(cycleTimes, 0.50),
                P75CycleTime = Percentile(cycleTimes, 0.75),
                MedianTimeToFirstComment = Percentile(firstCommentTimes, 0.50),
                P75TimeToFirstComment = Percentile(firstCommentTimes, 0.75),
                MedianTimeToFirstApproval = Percentile(firstApprovalTimes, 0.50),
                P75TimeToFirstApproval = Percentile(firstApprovalTimes, 0.75),
            };

            var prsPerWeek = report.Days > 0
                ? report.TeamMetrics.CompletedPrCount / (report.Days / 7.0)
                : 0;

            var uniqueContributors = report.Metrics
                .Select(m => m.AuthorDisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            entries.Add(new TeamComparisonEntry
            {
                Label = label,
                Report = report,
                Percentiles = percentiles,
                PrsPerWeek = prsPerWeek,
                UniqueContributorCount = uniqueContributors,
            });
        }

        return entries;
    }

    internal static TimeSpan? Percentile(List<TimeSpan> values, double p)
    {
        if (values.Count == 0)
            return null;

        var sorted = values.OrderBy(v => v).ToList();
        var index = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var fraction = index - lower;

        if (lower == upper)
            return sorted[lower];

        var lowerTicks = sorted[lower].Ticks;
        var upperTicks = sorted[upper].Ticks;
        return TimeSpan.FromTicks((long)(lowerTicks + (upperTicks - lowerTicks) * fraction));
    }
}
