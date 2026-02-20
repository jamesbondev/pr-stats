using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class BuildActivityCharts
{
    public static IEnumerable<GenericChart> Create(
        List<PullRequestMetrics> prMetrics,
        Dictionary<int, List<BuildInfo>>? buildsByPr)
    {
        var prsWithBuilds = prMetrics
            .Where(m => m.BuildMetrics != null)
            .ToList();

        if (prsWithBuilds.Count == 0)
            yield break;

        // Build count distribution histogram
        yield return CreateBuildCountHistogram(prsWithBuilds);

        // Build success rate by pipeline
        var allPipelines = prsWithBuilds
            .SelectMany(m => m.BuildMetrics!.PerPipeline)
            .GroupBy(p => p.DefinitionName)
            .ToList();
        if (allPipelines.Count > 0)
            yield return CreatePipelineSuccessRateBar(allPipelines);

        // Build duration over time
        if (buildsByPr != null)
        {
            var allBuilds = buildsByPr.Values.SelectMany(b => b).ToList();
            if (allBuilds.Any(b => b.StartTime.HasValue && b.FinishTime.HasValue))
                yield return CreateBuildDurationScatter(allBuilds);
        }

        // Builds per PR vs cycle time correlation
        var completedWithBuilds = prsWithBuilds
            .Where(m => m.Status == PrStatus.Completed && m.TotalCycleTime.HasValue)
            .ToList();
        if (completedWithBuilds.Count > 0)
            yield return CreateBuildsVsCycleTimeScatter(completedWithBuilds);
    }

    private static GenericChart CreateBuildCountHistogram(List<PullRequestMetrics> prsWithBuilds)
    {
        var counts = prsWithBuilds
            .Select(m => (double)m.BuildMetrics!.TotalBuildCount)
            .ToArray();

        return CSharpChart.Histogram<double, double, string>(X: counts)
            .WithMarkerStyle(Color: Color.fromHex("#3b82f6"))
            .WithTitle("Build Count Distribution (Builds per PR)")
            .WithXAxisStyle(Title.init("Number of Builds"))
            .WithYAxisStyle(Title.init("Number of PRs"));
    }

    private static GenericChart CreatePipelineSuccessRateBar(
        List<IGrouping<string, PipelineSummary>> allPipelines)
    {
        var pipelines = allPipelines
            .Select(g =>
            {
                int succ = g.Sum(p => p.SucceededCount);
                int fail = g.Sum(p => p.FailedCount);
                int total = succ + fail;
                return new
                {
                    Name = g.Key,
                    SuccessRate = total > 0 ? (double)succ / total * 100 : 0,
                    TotalRuns = g.Sum(p => p.RunCount),
                };
            })
            .OrderByDescending(p => p.TotalRuns)
            .Take(15)
            .ToList();

        var names = pipelines.Select(p => p.Name).ToArray();
        var rates = pipelines.Select(p => p.SuccessRate).ToArray();

        return CSharpChart.Bar<double, string, string>(
                values: rates,
                Keys: names,
                Name: "Success Rate")
            .WithMarkerStyle(Color: Color.fromHex("#10b981"))
            .WithTitle("Build Success Rate by Pipeline (%)")
            .WithXAxisStyle(Title.init("Success Rate (%)"))
            .WithYAxisStyle(Title.init("Pipeline"));
    }

    private static GenericChart CreateBuildDurationScatter(List<BuildInfo> allBuilds)
    {
        var completedBuilds = allBuilds
            .Where(b => b.StartTime.HasValue && b.FinishTime.HasValue)
            .OrderBy(b => b.QueueTime)
            .ToList();

        var dates = completedBuilds.Select(b => b.QueueTime).ToArray();
        var durations = completedBuilds
            .Select(b => (b.FinishTime!.Value - b.StartTime!.Value).TotalMinutes)
            .ToArray();

        return CSharpChart.Point<DateTime, double, string>(
                x: dates,
                y: durations,
                Name: "Build Duration")
            .WithMarkerStyle(Color: Color.fromHex("#f59e0b"), Size: 4)
            .WithTitle("Build Duration Over Time")
            .WithXAxisStyle(Title.init("Date"))
            .WithYAxisStyle(Title.init("Duration (minutes)"));
    }

    private static GenericChart CreateBuildsVsCycleTimeScatter(List<PullRequestMetrics> prs)
    {
        var buildCounts = prs.Select(m => (double)m.BuildMetrics!.TotalBuildCount).ToArray();
        var cycleTimes = prs.Select(m => m.TotalCycleTime!.Value.TotalHours).ToArray();

        return CSharpChart.Point<double, double, string>(
                x: buildCounts,
                y: cycleTimes,
                Name: "PR")
            .WithMarkerStyle(Color: Color.fromHex("#8b5cf6"), Size: 6)
            .WithTitle("Builds per PR vs Cycle Time")
            .WithXAxisStyle(Title.init("Number of Builds"))
            .WithYAxisStyle(Title.init("Cycle Time (hours)"));
    }
}
