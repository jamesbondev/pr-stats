using System.Text;
using Plotly.NET;
using PrStats.Models;
using PrStats.Visualization.Charts;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization;

public static class ComparisonDashboardGenerator
{
    private const string DarkBg = "#1e293b";
    private const string DarkPlotBg = "#1e293b";
    private const string DarkText = "#e2e8f0";
    private const string DarkGrid = "#334155";

    public static string Generate(List<TeamComparisonEntry> teams)
    {
        var sb = new StringBuilder();
        AppendHtmlHead(sb, teams);

        sb.AppendLine("<body>");
        AppendHeader(sb, teams);
        AppendComparisonTable(sb, teams);
        AppendBenchmarkLegend(sb);
        AppendChartSection(sb, "Cycle Time Distribution", "cycle-compare",
            [ComparisonCharts.CreateCycleTimeBoxPlots(teams)]);
        AppendChartSection(sb, "Time to First Review", "review-compare",
            [ComparisonCharts.CreateFirstReviewBoxPlots(teams)]);
        AppendChartSection(sb, "Quality Metrics", "quality-compare",
            [ComparisonCharts.CreateQualityGroupedBars(teams)]);
        AppendChartSection(sb, "Throughput &amp; Size", "throughput-compare",
            [ComparisonCharts.CreateThroughputBars(teams)]);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendHtmlHead(StringBuilder sb, List<TeamComparisonEntry> teams)
    {
        var teamLabels = string.Join(" vs ", teams.Select(t => t.Label));
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>Team Comparison - ").Append(Encode(teamLabels)).AppendLine("</title>");
        sb.AppendLine("<script src=\"https://cdn.plot.ly/plotly-2.35.2.min.js\"></script>");
        sb.AppendLine("""
<style>
    :root {
        --bg: #0f172a;
        --surface: #1e293b;
        --border: #334155;
        --text: #e2e8f0;
        --text-muted: #94a3b8;
        --green: #10b981;
        --amber: #f59e0b;
        --red: #ef4444;
        --blue: #3b82f6;
    }
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        background: var(--bg);
        color: var(--text);
        padding: 2rem;
    }
    .header {
        text-align: center;
        margin-bottom: 2rem;
        padding-bottom: 1rem;
        border-bottom: 1px solid var(--border);
    }
    .header h1 { font-size: 1.8rem; margin-bottom: 0.5rem; }
    .header p { color: var(--text-muted); font-size: 0.9rem; }
    .section {
        margin-bottom: 2.5rem;
    }
    .section h2 {
        font-size: 1.3rem;
        margin-bottom: 1rem;
        padding-bottom: 0.5rem;
        border-bottom: 1px solid var(--border);
    }
    .chart-grid {
        display: grid;
        grid-template-columns: 1fr;
        gap: 1.5rem;
    }
    .chart-container {
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 1rem;
        min-height: 400px;
    }
    .comparison-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.9rem;
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: 8px;
    }
    .comparison-table th, .comparison-table td {
        padding: 0.7rem 1rem;
        text-align: right;
        border-bottom: 1px solid var(--border);
    }
    .comparison-table th {
        color: var(--text-muted);
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        background: var(--surface);
    }
    .comparison-table th:first-child, .comparison-table td:first-child {
        text-align: left;
        font-weight: 600;
    }
    .comparison-table tbody tr:hover { background: rgba(255,255,255,0.03); }
    .badge {
        display: inline-block;
        padding: 0.15rem 0.5rem;
        border-radius: 9999px;
        font-size: 0.7rem;
        font-weight: 600;
        margin-left: 0.4rem;
        vertical-align: middle;
    }
    .benchmark-legend {
        display: flex;
        gap: 1.5rem;
        flex-wrap: wrap;
        padding: 1rem;
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: 8px;
        margin-bottom: 2rem;
        font-size: 0.85rem;
    }
    .benchmark-legend .item {
        display: flex;
        align-items: center;
        gap: 0.4rem;
    }
    .benchmark-legend .dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        display: inline-block;
    }
</style>
""");
        sb.AppendLine("</head>");
    }

    private static void AppendHeader(StringBuilder sb, List<TeamComparisonEntry> teams)
    {
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>Team Comparison Report</h1>");
        sb.Append("<p>");
        for (var i = 0; i < teams.Count; i++)
        {
            if (i > 0) sb.Append(" vs ");
            sb.Append(Encode(teams[i].Label))
              .Append(" (").Append(teams[i].Report.Days).Append(" days)");
        }
        sb.Append(" | Generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendComparisonTable(StringBuilder sb, List<TeamComparisonEntry> teams)
    {
        sb.AppendLine("<div class=\"section\"><h2>Comparison Table</h2>");
        sb.AppendLine("<div style=\"overflow-x:auto;\">");
        sb.AppendLine("<table class=\"comparison-table\">");

        // Header row
        sb.Append("<thead><tr><th>Metric</th>");
        foreach (var team in teams)
            sb.Append("<th>").Append(Encode(team.Label)).Append("</th>");
        sb.AppendLine("</tr></thead>");

        sb.AppendLine("<tbody>");

        // Median Cycle Time (with benchmark badge)
        AppendMetricRow(sb, "Median Cycle Time", teams, t =>
        {
            var val = t.Report.TeamMetrics.MedianCycleTime;
            if (val == null) return "N/A";
            return FormatTimeSpan(val.Value) + BenchmarkBadge(IndustryBenchmarks.ClassifyCycleTime(val.Value));
        });

        // p75 Cycle Time
        AppendMetricRow(sb, "p75 Cycle Time", teams, t =>
        {
            var val = t.Percentiles.P75CycleTime;
            if (val == null) return "N/A";
            return FormatTimeSpan(val.Value) + BenchmarkBadge(IndustryBenchmarks.ClassifyCycleTime(val.Value));
        });

        // Median Time to First Comment (with benchmark badge)
        AppendMetricRow(sb, "Median Time to First Comment", teams, t =>
        {
            var val = t.Percentiles.MedianTimeToFirstComment;
            if (val == null) return "N/A";
            return FormatTimeSpan(val.Value) + BenchmarkBadge(IndustryBenchmarks.ClassifyFirstReviewTime(val.Value));
        });

        // First-Time Approval Rate
        AppendMetricRow(sb, "First-Time Approval Rate", teams,
            t => t.Report.TeamMetrics.FirstTimeApprovalRate.ToString("P0"));

        // Approval Reset Rate
        AppendMetricRow(sb, "Approval Reset Rate", teams,
            t => t.Report.TeamMetrics.ApprovalResetRate.ToString("P0"));

        // Abandoned Rate
        AppendMetricRow(sb, "Abandoned Rate", teams,
            t => t.Report.TeamMetrics.AbandonedRate.ToString("P0"));

        // Thread Resolution Rate
        AppendMetricRow(sb, "Thread Resolution Rate", teams,
            t => t.Report.TeamMetrics.ThreadResolutionRate.ToString("P0"));

        // PRs/Week
        AppendMetricRow(sb, "PRs/Week", teams,
            t => t.PrsPerWeek.ToString("F1"));

        // Avg Files Changed (no benchmark badge)
        AppendMetricRow(sb, "Avg Files Changed", teams,
            t => t.Report.TeamMetrics.AvgFilesChanged.ToString("F1"));

        // Unique Contributors
        AppendMetricRow(sb, "Unique Contributors", teams,
            t => t.UniqueContributorCount.ToString());

        // Total PRs / Completed PRs
        AppendMetricRow(sb, "Total PRs", teams,
            t => t.Report.TeamMetrics.TotalPrCount.ToString());

        AppendMetricRow(sb, "Completed PRs", teams,
            t => t.Report.TeamMetrics.CompletedPrCount.ToString());

        sb.AppendLine("</tbody></table></div></div>");
    }

    private static void AppendMetricRow(
        StringBuilder sb, string metricName, List<TeamComparisonEntry> teams,
        Func<TeamComparisonEntry, string> valueSelector)
    {
        sb.Append("<tr><td>").Append(Encode(metricName)).Append("</td>");
        foreach (var team in teams)
            sb.Append("<td>").Append(valueSelector(team)).Append("</td>");
        sb.AppendLine("</tr>");
    }

    private static string BenchmarkBadge(BenchmarkTier tier)
    {
        var color = IndustryBenchmarks.TierColor(tier);
        var label = IndustryBenchmarks.TierLabel(tier);
        return $" <span class=\"badge\" style=\"background:{color};color:#fff;\">{Encode(label)}</span>";
    }

    private static void AppendBenchmarkLegend(StringBuilder sb)
    {
        sb.AppendLine("<div class=\"section\"><h2>Industry Benchmarks (LinearB)</h2>");
        sb.AppendLine("<div class=\"benchmark-legend\">");

        AppendLegendItem(sb, BenchmarkTier.Elite,
            $"Cycle Time \u2264{IndustryBenchmarks.CycleTimeElite.TotalHours}h, " +
            $"First Review \u2264{IndustryBenchmarks.FirstReviewElite.TotalMinutes}m");
        AppendLegendItem(sb, BenchmarkTier.Good,
            $"Cycle Time \u2264{IndustryBenchmarks.CycleTimeGood.TotalHours}h, " +
            $"First Review \u2264{IndustryBenchmarks.FirstReviewGood.TotalHours}h");
        AppendLegendItem(sb, BenchmarkTier.Fair,
            $"Cycle Time \u2264{IndustryBenchmarks.CycleTimeFair.TotalHours}h, " +
            $"First Review \u2264{IndustryBenchmarks.FirstReviewFair.TotalHours}h");
        AppendLegendItem(sb, BenchmarkTier.NeedsFocus,
            $"Cycle Time >{IndustryBenchmarks.CycleTimeFair.TotalHours}h, " +
            $"First Review >{IndustryBenchmarks.FirstReviewFair.TotalHours}h");

        sb.AppendLine("</div>");
        sb.AppendLine("<p style=\"color:var(--text-muted);font-size:0.8rem;margin-top:0.5rem;\">" +
            "File count benchmarks are not shown \u2014 LinearB thresholds use lines-of-code, " +
            "but this tool tracks files changed.</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendLegendItem(StringBuilder sb, BenchmarkTier tier, string description)
    {
        var color = IndustryBenchmarks.TierColor(tier);
        var label = IndustryBenchmarks.TierLabel(tier);
        sb.Append("<div class=\"item\"><span class=\"dot\" style=\"background:")
          .Append(color).Append(";\"></span><strong>").Append(Encode(label))
          .Append("</strong>: ").Append(Encode(description)).AppendLine("</div>");
    }

    private static void AppendChartSection(
        StringBuilder sb, string title, string id, List<GenericChart> charts)
    {
        if (charts.Count == 0)
            return;

        sb.Append("<div class=\"section\" id=\"").Append(id).Append("\"><h2>")
          .Append(title).AppendLine("</h2><div class=\"chart-grid\">");

        int chartIndex = 0;
        foreach (var chart in charts)
        {
            var divId = "chart-" + id + "-" + chartIndex++;
            sb.Append("<div class=\"chart-container\" id=\"").Append(divId).AppendLine("\"></div>");
        }

        sb.AppendLine("</div>");

        chartIndex = 0;
        foreach (var chart in charts)
        {
            var divId = "chart-" + id + "-" + chartIndex++;
            var figureJson = Plotly.NET.GenericChart.toFigureJson(chart);
            sb.AppendLine("<script>");
            sb.Append("(function() { var fig = ").Append(figureJson).AppendLine(";");
            sb.Append("Object.assign(fig.layout, {paper_bgcolor:'").Append(DarkBg)
              .Append("',plot_bgcolor:'").Append(DarkPlotBg)
              .Append("',font:{color:'").Append(DarkText)
              .Append("'},xaxis:Object.assign(fig.layout.xaxis||{},{gridcolor:'").Append(DarkGrid)
              .Append("'}),yaxis:Object.assign(fig.layout.yaxis||{},{gridcolor:'").Append(DarkGrid)
              .AppendLine("'})});");
            sb.Append("Plotly.newPlot('").Append(divId).AppendLine("',fig.data,fig.layout,{responsive:true});");
            sb.AppendLine("})();</script>");
        }

        sb.AppendLine("</div>");
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return ts.TotalDays.ToString("F1") + "d";
        if (ts.TotalHours >= 1)
            return ts.TotalHours.ToString("F1") + "h";
        return ts.TotalMinutes.ToString("F0") + "m";
    }

    private static string Encode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
