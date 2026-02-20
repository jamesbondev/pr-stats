using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class ThroughputCharts
{
    public static IEnumerable<GenericChart> Create(List<PullRequestMetrics> metrics)
    {
        var completed = metrics
            .Where(m => m.Status == PrStatus.Completed && m.ClosedDate.HasValue)
            .ToList();

        if (completed.Count == 0)
            yield break;

        yield return CreateWeeklyThroughputBar(completed);
    }

    private static GenericChart CreateWeeklyThroughputBar(List<PullRequestMetrics> completed)
    {
        var weeklyGroups = completed
            .GroupBy(m => StartOfWeek(m.ClosedDate!.Value))
            .OrderBy(g => g.Key)
            .ToList();

        var weeks = weeklyGroups.Select(g => g.Key.ToString("yyyy-MM-dd")).ToArray();
        var counts = weeklyGroups.Select(g => g.Count()).ToArray();

        return CSharpChart.Column<int, string, string>(
                values: counts,
                Keys: weeks,
                Name: "PRs Merged")
            .WithMarkerStyle(Color: Color.fromHex("#10b981"))
            .WithTitle("PRs Merged per Week")
            .WithXAxisStyle(Title.init("Week Starting"))
            .WithYAxisStyle(Title.init("PRs Merged"));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}
