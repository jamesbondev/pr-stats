using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class SizeDistributionCharts
{
    public static IEnumerable<GenericChart> Create(List<PullRequestMetrics> metrics)
    {
        if (metrics.Count == 0)
            yield break;

        // Histogram of files changed
        yield return CreateFilesChangedHistogram(metrics);

        // Scatter: size vs review time
        var withReviewTime = metrics
            .Where(m => m.TotalCycleTime.HasValue && m.FilesChanged > 0)
            .ToList();

        if (withReviewTime.Count > 0)
            yield return CreateSizeVsReviewTimeScatter(withReviewTime);
    }

    private static GenericChart CreateFilesChangedHistogram(List<PullRequestMetrics> metrics)
    {
        var files = metrics.Select(m => (double)m.FilesChanged).ToArray();

        return CSharpChart.Histogram<double, double, string>(X: files)
            .WithMarkerStyle(Color: Color.fromHex("#8b5cf6"))
            .WithTitle("Files Changed per PR")
            .WithXAxisStyle(Title.init("Files Changed"))
            .WithYAxisStyle(Title.init("Number of PRs"));
    }

    private static GenericChart CreateSizeVsReviewTimeScatter(List<PullRequestMetrics> metrics)
    {
        var files = metrics.Select(m => m.FilesChanged).ToArray();
        var hours = metrics.Select(m => m.TotalCycleTime!.Value.TotalHours).ToArray();

        return Chart2D.Chart.Scatter<int, double, string>(
                x: files,
                y: hours,
                mode: StyleParam.Mode.Markers,
                Name: "PRs")
            .WithMarkerStyle(Size: 6, Color: Color.fromHex("#8b5cf6"))
            .WithTitle("PR Size vs Cycle Time")
            .WithXAxisStyle(Title.init("Files Changed"))
            .WithYAxisStyle(Title.init("Cycle Time (hours)"));
    }
}
