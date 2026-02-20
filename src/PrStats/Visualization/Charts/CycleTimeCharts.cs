using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class CycleTimeCharts
{
    public static IEnumerable<GenericChart> Create(List<PullRequestMetrics> metrics)
    {
        var completed = metrics
            .Where(m => m.Status == PrStatus.Completed && !m.IsDraft && m.TotalCycleTime.HasValue)
            .OrderBy(m => m.CreationDate)
            .ToList();

        if (completed.Count == 0)
            yield break;

        // Box plot of cycle time phases
        yield return CreatePhaseBoxPlot(completed);

        // Scatter trend of total cycle time over time
        yield return CreateTrendScatter(completed);
    }

    private static GenericChart CreatePhaseBoxPlot(List<PullRequestMetrics> completed)
    {
        var charts = new List<GenericChart>();

        var totalHours = completed
            .Where(m => m.TotalCycleTime.HasValue)
            .Select(m => m.TotalCycleTime!.Value.TotalHours)
            .ToArray();

        if (totalHours.Length > 0)
        {
            charts.Add(CSharpChart.BoxPlot<double, double, string>(
                Y: totalHours,
                Name: "Total Cycle Time",
                MarkerColor: Color.fromHex("#3b82f6")));
        }

        var firstCommentHours = completed
            .Where(m => m.TimeToFirstHumanComment.HasValue)
            .Select(m => m.TimeToFirstHumanComment!.Value.TotalHours)
            .ToArray();

        if (firstCommentHours.Length > 0)
        {
            charts.Add(CSharpChart.BoxPlot<double, double, string>(
                Y: firstCommentHours,
                Name: "Time to First Comment",
                MarkerColor: Color.fromHex("#10b981")));
        }

        var firstApprovalHours = completed
            .Where(m => m.TimeToFirstApproval.HasValue)
            .Select(m => m.TimeToFirstApproval!.Value.TotalHours)
            .ToArray();

        if (firstApprovalHours.Length > 0)
        {
            charts.Add(CSharpChart.BoxPlot<double, double, string>(
                Y: firstApprovalHours,
                Name: "Time to First Approval",
                MarkerColor: Color.fromHex("#f59e0b")));
        }

        var approvalToMergeHours = completed
            .Where(m => m.TimeFromApprovalToMerge.HasValue)
            .Select(m => m.TimeFromApprovalToMerge!.Value.TotalHours)
            .ToArray();

        if (approvalToMergeHours.Length > 0)
        {
            charts.Add(CSharpChart.BoxPlot<double, double, string>(
                Y: approvalToMergeHours,
                Name: "Approval to Merge",
                MarkerColor: Color.fromHex("#ef4444")));
        }

        return CSharpChart.Combine(charts)
            .WithTitle("Cycle Time Phases (hours)")
            .WithYAxisStyle(Title.init("Hours"));
    }

    private static GenericChart CreateTrendScatter(List<PullRequestMetrics> completed)
    {
        var dates = completed.Select(m => m.CreationDate).ToArray();
        var hours = completed.Select(m => m.TotalCycleTime!.Value.TotalHours).ToArray();

        return Chart2D.Chart.Scatter<DateTime, double, string>(
                x: dates,
                y: hours,
                mode: StyleParam.Mode.Markers,
                Name: "Cycle Time")
            .WithMarkerStyle(Size: 6, Color: Color.fromHex("#3b82f6"))
            .WithTitle("Cycle Time Trend (hours)")
            .WithXAxisStyle(Title.init("PR Creation Date"))
            .WithYAxisStyle(Title.init("Total Cycle Time (hours)"));
    }
}
