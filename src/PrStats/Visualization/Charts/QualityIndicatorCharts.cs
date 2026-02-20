using Plotly.NET;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class QualityIndicatorCharts
{
    public static IEnumerable<GenericChart> Create(
        List<PullRequestMetrics> metrics, TeamMetrics teamMetrics)
    {
        var completed = metrics.Where(m => m.Status == PrStatus.Completed).ToList();
        if (completed.Count == 0)
            yield break;

        // Self-merge rate pie
        yield return CreateRatePie("Self-Merged PRs",
            completed.Count(m => m.IsSelfMerged),
            completed.Count(m => !m.IsSelfMerged),
            "Self-Merged", "Reviewed");

        // Unreviewed rate pie
        yield return CreateRatePie("Unreviewed PRs",
            completed.Count(m => m.IsUnreviewed),
            completed.Count(m => !m.IsUnreviewed),
            "Unreviewed", "Reviewed");

        // First-time approval rate pie
        yield return CreateRatePie("First-Time Approval",
            completed.Count(m => m.IsFirstTimeApproval),
            completed.Count(m => !m.IsFirstTimeApproval),
            "First-Time", "Required Changes");
    }

    private static GenericChart CreateRatePie(
        string title, int positiveCount, int negativeCount,
        string positiveLabel, string negativeLabel)
    {
        var labels = new[] { positiveLabel, negativeLabel };
        var values = new[] { positiveCount, negativeCount };
        var colors = new[] { Color.fromHex("#ef4444"), Color.fromHex("#10b981") };

        return CSharpChart.Pie<int, string, string>(
                values: values,
                Labels: labels)
            .WithMarkerStyle(Colors: colors)
            .WithTitle(title);
    }
}
