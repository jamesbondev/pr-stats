using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class ContributorCharts
{
    public static IEnumerable<GenericChart> Create(
        List<PullRequestMetrics> metrics, TeamMetrics teamMetrics)
    {
        // Top PR Creators bar chart
        if (teamMetrics.PrsPerAuthor.Count > 0)
            yield return CreateTopCreatorsBar(teamMetrics);

        // Per-Author Throughput grouped bar chart
        if (teamMetrics.ThroughputByAuthor.Count > 0)
            yield return CreatePerAuthorThroughput(teamMetrics);
    }

    private static GenericChart CreateTopCreatorsBar(TeamMetrics teamMetrics)
    {
        var sorted = teamMetrics.PrsPerAuthor
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .ToList();

        var names = sorted.Select(kv => kv.Key).ToArray();
        var counts = sorted.Select(kv => kv.Value).ToArray();

        return CSharpChart.Bar<int, string, string>(
                values: counts,
                Keys: names,
                Name: "PRs Created")
            .WithMarkerStyle(Color: Color.fromHex("#8b5cf6"))
            .WithTitle("Top PR Creators")
            .WithXAxisStyle(Title.init("PRs Created"))
            .WithYAxisStyle(Title.init("Author"));
    }

    private static GenericChart CreatePerAuthorThroughput(TeamMetrics teamMetrics)
    {
        // Get all unique weeks across all authors, sorted
        var allWeeks = teamMetrics.ThroughputByAuthor
            .SelectMany(kv => kv.Value)
            .Select(wc => wc.WeekStart)
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        var weekLabels = allWeeks.Select(w => w.ToString("yyyy-MM-dd")).ToArray();

        // Take top 10 authors by total throughput to avoid chart clutter
        var topAuthors = teamMetrics.ThroughputByAuthor
            .OrderByDescending(kv => kv.Value.Sum(wc => wc.Count))
            .Take(10)
            .ToList();

        var traces = new List<GenericChart>();
        var colors = new[] { "#8b5cf6", "#10b981", "#f59e0b", "#ef4444", "#3b82f6",
                             "#ec4899", "#14b8a6", "#f97316", "#6366f1", "#84cc16" };

        for (int i = 0; i < topAuthors.Count; i++)
        {
            var author = topAuthors[i];
            var weekLookup = author.Value.ToDictionary(wc => wc.WeekStart, wc => wc.Count);
            var counts = allWeeks.Select(w => weekLookup.TryGetValue(w, out var c) ? c : 0).ToArray();

            traces.Add(
                CSharpChart.Column<int, string, string>(
                    values: counts,
                    Keys: weekLabels,
                    Name: author.Key)
                .WithMarkerStyle(Color: Color.fromHex(colors[i % colors.Length])));
        }

        return CSharpChart.Combine(traces)
            .WithTitle("Per-Author Weekly Throughput (Top 10)")
            .WithXAxisStyle(Title.init("Week Starting"))
            .WithYAxisStyle(Title.init("PRs Merged"))
            .WithLayout(Layout.init<string>(BarMode: StyleParam.BarMode.Stack));
    }
}
