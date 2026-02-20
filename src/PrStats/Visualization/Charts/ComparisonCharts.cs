using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class ComparisonCharts
{
    private static readonly string[] TeamColors =
        ["#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6"];

    public static GenericChart CreateCycleTimeBoxPlots(List<TeamComparisonEntry> teams)
    {
        var traces = new List<GenericChart>();

        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var hours = team.Report.Metrics
                .Where(m => m.Status == PrStatus.Completed && !m.IsDraft && m.TotalCycleTime.HasValue)
                .Select(m => m.TotalCycleTime!.Value.TotalHours)
                .ToArray();

            if (hours.Length > 0)
            {
                traces.Add(CSharpChart.BoxPlot<double, double, string>(
                    Y: hours,
                    Name: team.Label,
                    MarkerColor: Color.fromHex(TeamColors[i % TeamColors.Length])));
            }
        }

        // Benchmark lines as invisible scatter traces with named legend entries
        AddBenchmarkLine(traces, IndustryBenchmarks.CycleTimeElite.TotalHours,
            $"Elite \u226426h", "#10b981");
        AddBenchmarkLine(traces, IndustryBenchmarks.CycleTimeGood.TotalHours,
            $"Good \u226480h", "#3b82f6");
        AddBenchmarkLine(traces, IndustryBenchmarks.CycleTimeFair.TotalHours,
            $"Fair \u2264167h", "#f59e0b");

        return CSharpChart.Combine(traces)
            .WithTitle("Cycle Time Distribution (hours)")
            .WithYAxisStyle(Title.init("Hours"));
    }

    public static GenericChart CreateFirstReviewBoxPlots(List<TeamComparisonEntry> teams)
    {
        var traces = new List<GenericChart>();

        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var hours = team.Report.Metrics
                .Where(m => m.Status == PrStatus.Completed && !m.IsDraft
                    && m.TimeToFirstHumanComment.HasValue)
                .Select(m => m.TimeToFirstHumanComment!.Value.TotalHours)
                .ToArray();

            if (hours.Length > 0)
            {
                traces.Add(CSharpChart.BoxPlot<double, double, string>(
                    Y: hours,
                    Name: team.Label,
                    MarkerColor: Color.fromHex(TeamColors[i % TeamColors.Length])));
            }
        }

        AddBenchmarkLine(traces, IndustryBenchmarks.FirstReviewElite.TotalHours,
            $"Elite \u226475m", "#10b981");
        AddBenchmarkLine(traces, IndustryBenchmarks.FirstReviewGood.TotalHours,
            $"Good \u22644h", "#3b82f6");
        AddBenchmarkLine(traces, IndustryBenchmarks.FirstReviewFair.TotalHours,
            $"Fair \u226412h", "#f59e0b");

        return CSharpChart.Combine(traces)
            .WithTitle("Time to First Review (hours)")
            .WithYAxisStyle(Title.init("Hours"));
    }

    public static GenericChart CreateQualityGroupedBars(List<TeamComparisonEntry> teams)
    {
        var traces = new List<GenericChart>();
        var categories = new[] { "FTA Rate", "Abandoned Rate", "Reset Rate", "Thread Resolution" };

        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var tm = team.Report.TeamMetrics;
            var values = new[]
            {
                tm.FirstTimeApprovalRate * 100,
                tm.AbandonedRate * 100,
                tm.ApprovalResetRate * 100,
                tm.ThreadResolutionRate * 100,
            };

            traces.Add(CSharpChart.Column<double, string, string>(
                    values: values,
                    Keys: categories,
                    Name: team.Label)
                .WithMarkerStyle(Color: Color.fromHex(TeamColors[i % TeamColors.Length])));
        }

        return CSharpChart.Combine(traces)
            .WithTitle("Quality Metrics (%)")
            .WithYAxisStyle(Title.init("Percentage"))
            .WithLayout(Layout.init<string>(BarMode: StyleParam.BarMode.Group));
    }

    public static GenericChart CreateThroughputBars(List<TeamComparisonEntry> teams)
    {
        var traces = new List<GenericChart>();
        var categories = new[] { "PRs/Week", "Avg Files Changed" };

        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var values = new[]
            {
                team.PrsPerWeek,
                team.Report.TeamMetrics.AvgFilesChanged,
            };

            traces.Add(CSharpChart.Column<double, string, string>(
                    values: values,
                    Keys: categories,
                    Name: team.Label)
                .WithMarkerStyle(Color: Color.fromHex(TeamColors[i % TeamColors.Length])));
        }

        return CSharpChart.Combine(traces)
            .WithTitle("Throughput & Size")
            .WithYAxisStyle(Title.init("Value"))
            .WithLayout(Layout.init<string>(BarMode: StyleParam.BarMode.Group));
    }

    private static void AddBenchmarkLine(List<GenericChart> traces, double yValue,
        string name, string color)
    {
        // Use a scatter trace with mode="lines" to draw horizontal benchmark lines
        // xaxis range from -0.5 to 10 to cover the box plot area
        traces.Add(Chart2D.Chart.Scatter<double, double, string>(
                x: new[] { -0.5, 10.0 },
                y: new[] { yValue, yValue },
                mode: StyleParam.Mode.Lines,
                Name: name)
            .WithLineStyle(Color: Color.fromHex(color),
                Dash: StyleParam.DrawingStyle.Dash,
                Width: 1.5));
    }
}
