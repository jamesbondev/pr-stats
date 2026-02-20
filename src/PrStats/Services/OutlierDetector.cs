using PrStats.Models;

namespace PrStats.Services;

public static class OutlierDetector
{
    private const double BadThreshold = 1.5;
    private const double WarnThreshold = 1.0;
    private const int MaxResults = 10;
    private const int MinPrs = 3;

    public static List<OutlierPrResult> Detect(List<PullRequestMetrics> allMetrics)
    {
        var completed = allMetrics
            .Where(m => m.Status == PrStatus.Completed && !m.IsDraft && !m.IsAuthorBot)
            .ToList();

        if (completed.Count < MinPrs)
            return [];

        var dimensions = BuildDimensions(completed);
        var results = new List<OutlierPrResult>();

        foreach (var pr in completed)
        {
            var flags = new List<OutlierFlag>();
            double compositeScore = 0;

            foreach (var dim in dimensions)
            {
                var value = dim.Extractor(pr);
                if (value is null)
                    continue;

                var z = (value.Value - dim.Mean) / dim.Stddev;
                if (z < WarnThreshold)
                    continue;

                compositeScore += z;
                flags.Add(new OutlierFlag
                {
                    Label = dim.Label,
                    CssClass = z >= BadThreshold ? "bad" : "warn",
                    ZScore = z,
                });
            }

            if (flags.Any(f => f.CssClass == "bad"))
            {
                results.Add(new OutlierPrResult
                {
                    Metrics = pr,
                    CompositeScore = compositeScore,
                    Flags = flags.OrderByDescending(f => f.ZScore).ToList(),
                });
            }
        }

        return results
            .OrderByDescending(r => r.CompositeScore)
            .Take(MaxResults)
            .ToList();
    }

    private static List<Dimension> BuildDimensions(List<PullRequestMetrics> completed)
    {
        var dimensions = new List<Dimension>();

        AddDimension(dimensions, "Slow Cycle", completed,
            m => m.TotalCycleTime?.TotalHours);
        AddDimension(dimensions, "Slow Review", completed,
            m => m.TimeToFirstHumanComment?.TotalHours);
        AddDimension(dimensions, "Large PR", completed,
            m => m.FilesChanged);
        AddDimension(dimensions, "High Churn", completed,
            m => m.IterationCount);
        AddDimension(dimensions, "Contentious", completed,
            m => m.HumanCommentCount);
        AddDimension(dimensions, "Approval Resets", completed,
            m => m.ApprovalResetCount);

        if (completed.Any(m => m.BuildMetrics != null))
        {
            AddDimension(dimensions, "Build Failures", completed,
                m => m.BuildMetrics?.FailedCount);
            AddDimension(dimensions, "Many Builds", completed,
                m => m.BuildMetrics?.TotalBuildCount);
        }

        return dimensions;
    }

    private static void AddDimension(
        List<Dimension> dimensions,
        string label,
        List<PullRequestMetrics> completed,
        Func<PullRequestMetrics, double?> extractor)
    {
        var values = completed
            .Select(extractor)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count < 2)
            return;

        var mean = values.Average();
        var variance = values.Average(v => (v - mean) * (v - mean));
        var stddev = Math.Sqrt(variance);

        if (stddev == 0)
            return;

        dimensions.Add(new Dimension(label, mean, stddev, extractor));
    }

    private sealed record Dimension(
        string Label,
        double Mean,
        double Stddev,
        Func<PullRequestMetrics, double?> Extractor);
}
