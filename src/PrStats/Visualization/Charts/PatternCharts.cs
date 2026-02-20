using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class PatternCharts
{
    public static IEnumerable<GenericChart> Create(List<PullRequestMetrics> metrics)
    {
        if (metrics.Count == 0)
            yield break;

        yield return CreateDayOfWeekBar(metrics);
        yield return CreateHourOfDayBar(metrics);
    }

    private static GenericChart CreateDayOfWeekBar(List<PullRequestMetrics> metrics)
    {
        var dayOrder = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        };

        var groups = metrics
            .GroupBy(m => m.CreationDayOfWeek)
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = dayOrder.Select(d => d.ToString()).ToArray();
        var counts = dayOrder.Select(d => groups.GetValueOrDefault(d, 0)).ToArray();

        return CSharpChart.Column<int, string, string>(
                values: counts,
                Keys: labels,
                Name: "PRs Created")
            .WithMarkerStyle(Color: Color.fromHex("#6366f1"))
            .WithTitle("PRs by Day of Week")
            .WithXAxisStyle(Title.init("Day"))
            .WithYAxisStyle(Title.init("PRs Created"));
    }

    private static GenericChart CreateHourOfDayBar(List<PullRequestMetrics> metrics)
    {
        var groups = metrics
            .GroupBy(m => m.CreationHourOfDay)
            .ToDictionary(g => g.Key, g => g.Count());

        var hours = Enumerable.Range(0, 24).ToArray();
        var labels = hours.Select(h => $"{h:D2}:00").ToArray();
        var counts = hours.Select(h => groups.GetValueOrDefault(h, 0)).ToArray();

        return CSharpChart.Column<int, string, string>(
                values: counts,
                Keys: labels,
                Name: "PRs Created")
            .WithMarkerStyle(Color: Color.fromHex("#6366f1"))
            .WithTitle("PRs by Hour of Day (UTC)")
            .WithXAxisStyle(Title.init("Hour"))
            .WithYAxisStyle(Title.init("PRs Created"));
    }
}
