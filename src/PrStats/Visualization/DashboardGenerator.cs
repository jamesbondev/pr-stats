using System.Text;
using Plotly.NET;
using PrStats.Configuration;
using PrStats.Models;
using PrStats.Services;
using PrStats.Visualization.Charts;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization;

public static class DashboardGenerator
{
    private const string DarkBg = "#1e293b";
    private const string DarkPlotBg = "#1e293b";
    private const string DarkText = "#e2e8f0";
    private const string DarkGrid = "#334155";

    public static string Generate(
        AppSettings settings,
        List<PullRequestData> prData,
        List<PullRequestMetrics> prMetrics,
        TeamMetrics teamMetrics,
        Dictionary<int, List<BuildInfo>>? buildsByPr = null)
    {
        var sb = new StringBuilder();
        AppendHtmlHead(sb, settings);

        sb.AppendLine("<body>");
        AppendHeader(sb, settings, teamMetrics);
        AppendExecutiveSummary(sb, teamMetrics, prMetrics);
        AppendOutlierPrs(sb, prMetrics, teamMetrics);
        AppendRepositoryBreakdown(sb, teamMetrics, prMetrics);
        AppendChartSection(sb, "Cycle Time Analysis", "cycle-time",
            CycleTimeCharts.Create(prMetrics));
        AppendChartSection(sb, "PR Size Distribution", "size",
            SizeDistributionCharts.Create(prMetrics));
        AppendChartSection(sb, "Throughput", "throughput",
            ThroughputCharts.Create(prMetrics));
        AppendChartSection(sb, "Review Activity", "review",
            ReviewActivityCharts.Create(prMetrics, teamMetrics));
        AppendChartSection(sb, "Team Collaboration", "collaboration",
            TeamCollaborationCharts.Create(teamMetrics));
        AppendChartSection(sb, "Quality Indicators", "quality",
            QualityIndicatorCharts.Create(prMetrics, teamMetrics));
        AppendChartSection(sb, "Temporal Patterns", "patterns",
            PatternCharts.Create(prMetrics));
        if (teamMetrics.BuildMetrics != null)
        {
            AppendBuildSummary(sb, teamMetrics.BuildMetrics);
            AppendChartSection(sb, "CI/Build Activity", "build-activity",
                BuildActivityCharts.Create(prMetrics, buildsByPr));
        }
        AppendChartSection(sb, "Individual Contributors", "contributors",
            ContributorCharts.Create(prMetrics, teamMetrics));
        AppendContributorTable(sb, prMetrics, teamMetrics);
        AppendStatusDistribution(sb, teamMetrics);
        AppendActiveAgeSummary(sb, prMetrics);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public static string GenerateEmpty(AppSettings settings)
    {
        var sb = new StringBuilder();
        AppendHtmlHead(sb, settings);
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>PR Statistics Dashboard</h1>");
        sb.Append("<p>").Append(Encode(settings.RepositoryDisplayName))
          .Append(" | ").Append(settings.Days).Append("-day lookback | Generated ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine("</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"empty-message\">");
        sb.Append("<h2>No pull requests found in the last ").Append(settings.Days).AppendLine(" days</h2>");
        sb.AppendLine("<p>Try increasing the --days parameter or verify the repository name.</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendHtmlHead(StringBuilder sb, AppSettings settings)
    {
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>PR Stats - ").Append(Encode(settings.RepositoryDisplayName)).AppendLine("</title>");
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
    .kpi-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 1rem;
        margin-bottom: 2rem;
    }
    .kpi-card {
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 1.2rem;
        text-align: center;
    }
    .kpi-card .label { color: var(--text-muted); font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; }
    .kpi-card .value { font-size: 1.8rem; font-weight: 700; margin: 0.3rem 0; }
    .kpi-card .sub { color: var(--text-muted); font-size: 0.8rem; }
    .kpi-green .value { color: var(--green); }
    .kpi-amber .value { color: var(--amber); }
    .kpi-red .value { color: var(--red); }
    .kpi-blue .value { color: var(--blue); }
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
        grid-template-columns: repeat(auto-fit, minmax(500px, 1fr));
        gap: 1.5rem;
    }
    .chart-container {
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 1rem;
        min-height: 400px;
    }
    .contributor-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.9rem;
    }
    .contributor-table th, .contributor-table td {
        padding: 0.6rem 0.8rem;
        text-align: right;
        border-bottom: 1px solid var(--border);
    }
    .contributor-table th {
        color: var(--text-muted);
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        position: sticky;
        top: 0;
        background: var(--surface);
        cursor: pointer;
    }
    .contributor-table th:hover { color: var(--text); }
    .contributor-table th:first-child, .contributor-table td:first-child { text-align: left; }
    .contributor-table tbody tr:hover { background: rgba(255,255,255,0.03); }
    .contributor-table .good { color: var(--green); }
    .contributor-table .warn { color: var(--amber); }
    .contributor-table .bad { color: var(--red); }
    .empty-message {
        text-align: center;
        padding: 4rem 2rem;
        color: var(--text-muted);
    }
    .empty-message h2 { margin-bottom: 1rem; }
    .outlier-flag {
        display: inline-block;
        padding: 0.15rem 0.5rem;
        border-radius: 9999px;
        font-size: 0.7rem;
        font-weight: 600;
        margin: 0.1rem 0.15rem;
        white-space: nowrap;
    }
    .outlier-flag.bad { background: rgba(239,68,68,0.2); color: var(--red); }
    .outlier-flag.warn { background: rgba(245,158,11,0.2); color: var(--amber); }
</style>
""");
        sb.AppendLine("</head>");
    }

    private static void AppendHeader(StringBuilder sb, AppSettings settings, TeamMetrics teamMetrics)
    {
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>PR Statistics Dashboard</h1>");
        sb.Append("<p>").Append(Encode(settings.RepositoryDisplayName))
          .Append(" | ").Append(settings.Days).Append("-day lookback | ")
          .Append(teamMetrics.TotalPrCount).Append(" PRs analyzed | Generated ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine("</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendExecutiveSummary(
        StringBuilder sb, TeamMetrics team, List<PullRequestMetrics> metrics)
    {
        sb.AppendLine("<div class=\"section\"><h2>Executive Summary</h2><div class=\"kpi-grid\">");

        var avgCycleStr = team.AvgCycleTime.HasValue
            ? FormatTimeSpan(team.AvgCycleTime.Value) : "N/A";
        var cycleClass = team.AvgCycleTime.HasValue
            ? (team.AvgCycleTime.Value.TotalHours <= 24 ? "kpi-green" :
               team.AvgCycleTime.Value.TotalHours <= 72 ? "kpi-amber" : "kpi-red")
            : "kpi-blue";
        AppendKpiCard(sb, "Avg Cycle Time", avgCycleStr, "completed PRs", cycleClass);

        var medCycleStr = team.MedianCycleTime.HasValue
            ? FormatTimeSpan(team.MedianCycleTime.Value) : "N/A";
        AppendKpiCard(sb, "Median Cycle Time", medCycleStr, "completed PRs", "kpi-blue");

        AppendKpiCard(sb, "Total PRs", team.TotalPrCount.ToString(),
            team.CompletedPrCount + " completed", "kpi-blue");

        AppendKpiCard(sb, "Avg Files Changed", team.AvgFilesChanged.ToString("F1"),
            "per PR", "kpi-blue");

        var abandonClass = team.AbandonedRate < 0.10 ? "kpi-green" :
            team.AbandonedRate < 0.25 ? "kpi-amber" : "kpi-red";
        AppendKpiCard(sb, "Abandoned Rate", team.AbandonedRate.ToString("P0"),
            team.AbandonedPrCount + " PRs", abandonClass);

        var ftaClass = team.FirstTimeApprovalRate >= 0.70 ? "kpi-green" :
            team.FirstTimeApprovalRate >= 0.50 ? "kpi-amber" : "kpi-red";
        AppendKpiCard(sb, "First-Time Approval", team.FirstTimeApprovalRate.ToString("P0"),
            "of completed PRs", ftaClass);

        var resetClass = team.ApprovalResetRate <= 0.15 ? "kpi-green" :
            team.ApprovalResetRate <= 0.30 ? "kpi-amber" : "kpi-red";
        AppendKpiCard(sb, "Approval Reset Rate", team.ApprovalResetRate.ToString("P0"),
            "of completed PRs", resetClass);

        sb.AppendLine("</div></div>");
    }

    private static void AppendOutlierPrs(
        StringBuilder sb, List<PullRequestMetrics> prMetrics, TeamMetrics teamMetrics)
    {
        var outliers = OutlierDetector.Detect(prMetrics);
        if (outliers.Count == 0)
            return;

        var hasBuilds = teamMetrics.BuildMetrics != null;
        var showRepos = teamMetrics.PerRepositoryBreakdown.Count > 1;

        sb.AppendLine("<div class=\"section\" id=\"outlier-prs\"><h2>Top PRs Worth Investigating</h2>");
        sb.AppendLine("<p style=\"color:var(--text-muted);font-size:0.85rem;margin-bottom:1rem;\">PRs with metrics significantly above team averages, ranked by composite z-score. Red flags indicate &ge;1.5 standard deviations above the mean; amber flags indicate &ge;1.0.</p>");
        sb.AppendLine("<div style=\"overflow-x:auto;background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:0.5rem;\">");
        sb.AppendLine("<table class=\"contributor-table\" id=\"outlier-tbl\">");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>PR</th>");
        sb.AppendLine("<th>Author</th>");
        if (showRepos) sb.AppendLine("<th>Repo</th>");
        sb.AppendLine("<th>Cycle Time</th>");
        sb.AppendLine("<th>Files</th>");
        sb.AppendLine("<th>Iterations</th>");
        sb.AppendLine("<th>Comments</th>");
        sb.AppendLine("<th>Resets</th>");
        if (hasBuilds) sb.AppendLine("<th>Failed Builds</th>");
        sb.AppendLine("<th>Score</th>");
        sb.AppendLine("<th style=\"text-align:left\">Flags</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var outlier in outliers)
        {
            var m = outlier.Metrics;
            var title = m.Title.Length > 60 ? m.Title[..57] + "..." : m.Title;

            sb.AppendLine("<tr>");
            sb.Append("<td style=\"text-align:left\">!").Append(m.PullRequestId)
              .Append(" ").Append(Encode(title)).AppendLine("</td>");
            sb.Append("<td style=\"text-align:left\">").Append(Encode(m.AuthorDisplayName)).AppendLine("</td>");
            if (showRepos)
                sb.Append("<td style=\"text-align:left\">").Append(Encode(m.RepositoryName)).AppendLine("</td>");
            sb.Append("<td>").Append(m.TotalCycleTime.HasValue ? FormatTimeSpan(m.TotalCycleTime.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(m.FilesChanged).AppendLine("</td>");
            sb.Append("<td>").Append(m.IterationCount).AppendLine("</td>");
            sb.Append("<td>").Append(m.HumanCommentCount).AppendLine("</td>");
            sb.Append("<td>").Append(m.ApprovalResetCount).AppendLine("</td>");
            if (hasBuilds)
                sb.Append("<td>").Append(m.BuildMetrics?.FailedCount.ToString() ?? "—").AppendLine("</td>");
            sb.Append("<td>").Append(outlier.CompositeScore.ToString("F1")).AppendLine("</td>");

            sb.Append("<td style=\"text-align:left\">");
            foreach (var flag in outlier.Flags)
            {
                sb.Append("<span class=\"outlier-flag ").Append(flag.CssClass).Append("\">")
                  .Append(Encode(flag.Label)).Append("</span>");
            }
            sb.AppendLine("</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div>");

        // Client-side sorting
        sb.AppendLine("""
<script>
(function() {
    var tbl = document.getElementById('outlier-tbl');
    var headers = tbl.querySelectorAll('th');
    headers.forEach(function(th, idx) {
        th.addEventListener('click', function() {
            var rows = Array.from(tbl.querySelectorAll('tbody tr'));
            var asc = th.dataset.asc === '1';
            rows.sort(function(a, b) {
                var aVal = a.children[idx].textContent.trim();
                var bVal = b.children[idx].textContent.trim();
                var aNum = parseFloat(aVal.replace(/[^\d.\-]/g, ''));
                var bNum = parseFloat(bVal.replace(/[^\d.\-]/g, ''));
                if (!isNaN(aNum) && !isNaN(bNum)) return asc ? aNum - bNum : bNum - aNum;
                return asc ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
            });
            rows.forEach(function(r) { tbl.querySelector('tbody').appendChild(r); });
            th.dataset.asc = asc ? '0' : '1';
        });
    });
})();
</script>
""");
        sb.AppendLine("</div>");
    }

    private static void AppendKpiCard(
        StringBuilder sb, string label, string value, string sub, string colorClass)
    {
        sb.Append("<div class=\"kpi-card ").Append(colorClass).AppendLine("\">");
        sb.Append("<div class=\"label\">").Append(Encode(label)).AppendLine("</div>");
        sb.Append("<div class=\"value\">").Append(Encode(value)).AppendLine("</div>");
        sb.Append("<div class=\"sub\">").Append(Encode(sub)).AppendLine("</div>");
        sb.AppendLine("</div>");
    }

    private static void AppendChartSection(
        StringBuilder sb, string title, string id, IEnumerable<GenericChart> charts)
    {
        var chartList = charts.ToList();
        if (chartList.Count == 0)
            return;

        sb.Append("<div class=\"section\" id=\"").Append(id).Append("\"><h2>")
          .Append(Encode(title)).AppendLine("</h2><div class=\"chart-grid\">");

        int chartIndex = 0;
        foreach (var chart in chartList)
        {
            var divId = "chart-" + id + "-" + chartIndex++;
            sb.Append("<div class=\"chart-container\" id=\"").Append(divId).AppendLine("\"></div>");
        }

        sb.AppendLine("</div>");

        chartIndex = 0;
        foreach (var chart in chartList)
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

    private static void AppendRepositoryBreakdown(
        StringBuilder sb, TeamMetrics teamMetrics, List<PullRequestMetrics> prMetrics)
    {
        if (teamMetrics.PerRepositoryBreakdown.Count <= 1)
            return;

        sb.AppendLine("<div class=\"section\" id=\"repo-breakdown\"><h2>Repository Breakdown</h2>");
        sb.AppendLine("<div style=\"overflow-x:auto;background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:0.5rem;margin-bottom:1.5rem;\">");
        sb.AppendLine("<table class=\"contributor-table\" id=\"repo-tbl\">");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Repository</th>");
        sb.AppendLine("<th>Total</th>");
        sb.AppendLine("<th>Completed</th>");
        sb.AppendLine("<th>Abandoned</th>");
        sb.AppendLine("<th>Active</th>");
        sb.AppendLine("<th>Abandoned Rate</th>");
        sb.AppendLine("<th>Avg Cycle</th>");
        sb.AppendLine("<th>Median Cycle</th>");
        sb.AppendLine("<th>Avg Files</th>");
        sb.AppendLine("<th>FTA Rate</th>");
        sb.AppendLine("<th>Reset Rate</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var (repoName, r) in teamMetrics.PerRepositoryBreakdown.OrderByDescending(kv => kv.Value.TotalPrCount))
        {
            var abandonClass = r.AbandonedRate < 0.10 ? "good" : r.AbandonedRate < 0.25 ? "warn" : "bad";
            var ftaClass = r.FirstTimeApprovalRate >= 0.70 ? "good" : r.FirstTimeApprovalRate >= 0.50 ? "warn" : "bad";
            var resetClass = r.ApprovalResetRate <= 0.15 ? "good" : r.ApprovalResetRate <= 0.30 ? "warn" : "bad";

            sb.AppendLine("<tr>");
            sb.Append("<td>").Append(Encode(repoName)).AppendLine("</td>");
            sb.Append("<td>").Append(r.TotalPrCount).AppendLine("</td>");
            sb.Append("<td>").Append(r.CompletedPrCount).AppendLine("</td>");
            sb.Append("<td>").Append(r.AbandonedPrCount).AppendLine("</td>");
            sb.Append("<td>").Append(r.ActivePrCount).AppendLine("</td>");
            sb.Append("<td class=\"").Append(abandonClass).Append("\">").Append(r.AbandonedRate.ToString("P0")).AppendLine("</td>");
            sb.Append("<td>").Append(r.AvgCycleTime.HasValue ? FormatTimeSpan(r.AvgCycleTime.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(r.MedianCycleTime.HasValue ? FormatTimeSpan(r.MedianCycleTime.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(r.AvgFilesChanged.ToString("F1")).AppendLine("</td>");
            sb.Append("<td class=\"").Append(ftaClass).Append("\">").Append(r.FirstTimeApprovalRate.ToString("P0")).AppendLine("</td>");
            sb.Append("<td class=\"").Append(resetClass).Append("\">").Append(r.ApprovalResetRate.ToString("P0")).AppendLine("</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div>");

        // Cycle time by repository box plot
        AppendCycleTimeByRepoChart(sb, prMetrics);

        sb.AppendLine("</div>");
    }

    private static void AppendCycleTimeByRepoChart(
        StringBuilder sb, List<PullRequestMetrics> prMetrics)
    {
        var repoGroups = prMetrics
            .Where(m => m.Status == PrStatus.Completed && !m.IsDraft && m.TotalCycleTime.HasValue)
            .GroupBy(m => m.RepositoryName)
            .Where(g => g.Any())
            .OrderByDescending(g => g.Count())
            .ToList();

        if (repoGroups.Count < 2)
            return;

        sb.AppendLine("<div class=\"chart-grid\">");
        sb.AppendLine("<div class=\"chart-container\" id=\"chart-repo-cycletime\"></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<script>");
        sb.Append("(function() { var data = [");

        var colors = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6", "#ec4899", "#06b6d4", "#84cc16" };
        int colorIdx = 0;
        foreach (var g in repoGroups)
        {
            var hours = string.Join(",", g.Select(m => m.TotalCycleTime!.Value.TotalHours.ToString("F2")));
            var color = colors[colorIdx % colors.Length];
            colorIdx++;
            sb.Append("{y:[").Append(hours).Append("],type:'box',name:'")
              .Append(EscapeJs(g.Key)).Append("',marker:{color:'").Append(color).Append("'}},");
        }

        sb.AppendLine("];");
        sb.Append("var layout = {title:'Cycle Time by Repository (hours)',")
          .Append("paper_bgcolor:'").Append(DarkBg)
          .Append("',plot_bgcolor:'").Append(DarkPlotBg)
          .Append("',font:{color:'").Append(DarkText)
          .Append("'},yaxis:{title:'Hours',gridcolor:'").Append(DarkGrid)
          .Append("'},xaxis:{gridcolor:'").Append(DarkGrid)
          .AppendLine("'}};");
        sb.AppendLine("Plotly.newPlot('chart-repo-cycletime',data,layout,{responsive:true});");
        sb.AppendLine("})();</script>");
    }

    private static void AppendBuildSummary(StringBuilder sb, TeamBuildMetrics buildMetrics)
    {
        sb.AppendLine("<div class=\"section\"><h2>CI/Build Summary</h2><div class=\"kpi-grid\">");

        AppendKpiCard(sb, "Avg Builds/PR",
            buildMetrics.AvgBuildsPerPr.ToString("F1"),
            $"{buildMetrics.TotalBuildsAcrossAllPrs} total builds", "kpi-blue");

        var successClass = buildMetrics.OverallBuildSuccessRate >= 0.90 ? "kpi-green" :
            buildMetrics.OverallBuildSuccessRate >= 0.75 ? "kpi-amber" : "kpi-red";
        AppendKpiCard(sb, "CI Success Rate",
            buildMetrics.OverallBuildSuccessRate.ToString("P0"),
            "terminal outcomes only", successClass);

        var runTimeStr = buildMetrics.AvgBuildRunTime.HasValue
            ? FormatTimeSpan(buildMetrics.AvgBuildRunTime.Value) : "N/A";
        AppendKpiCard(sb, "Avg Build Run Time", runTimeStr,
            "execution time", "kpi-blue");

        var queueTimeStr = buildMetrics.AvgQueueTime.HasValue
            ? FormatTimeSpan(buildMetrics.AvgQueueTime.Value) : "N/A";
        var queueClass = buildMetrics.AvgQueueTime.HasValue && buildMetrics.AvgQueueTime.Value.TotalMinutes <= 5
            ? "kpi-green" : buildMetrics.AvgQueueTime.HasValue && buildMetrics.AvgQueueTime.Value.TotalMinutes <= 15
            ? "kpi-amber" : "kpi-blue";
        AppendKpiCard(sb, "Avg Queue Time", queueTimeStr,
            "agent wait", queueClass);

        sb.AppendLine("</div></div>");
    }

    private static void AppendContributorTable(
        StringBuilder sb, List<PullRequestMetrics> metrics, TeamMetrics teamMetrics)
    {
        var hasBuilds = teamMetrics.BuildMetrics != null;
        var authors = metrics
            .Where(m => !m.IsAuthorBot)
            .GroupBy(m => m.AuthorDisplayName)
            .Select(g =>
            {
                var prs = g.ToList();
                var completed = prs.Where(p => p.Status == PrStatus.Completed && !p.IsDraft).ToList();
                var cycleTimes = completed.Where(p => p.TotalCycleTime.HasValue).Select(p => p.TotalCycleTime!.Value).ToList();
                var firstCommentTimes = completed.Where(p => p.TimeToFirstHumanComment.HasValue).Select(p => p.TimeToFirstHumanComment!.Value).ToList();
                var firstApprovalTimes = completed.Where(p => p.TimeToFirstApproval.HasValue).Select(p => p.TimeToFirstApproval!.Value).ToList();

                // Build metrics per author
                var prsWithBuilds = prs.Where(p => p.BuildMetrics != null).ToList();
                double? avgBuilds = prsWithBuilds.Count > 0
                    ? prsWithBuilds.Average(p => p.BuildMetrics!.TotalBuildCount) : null;
                double? ciSuccessRate = null;
                if (prsWithBuilds.Count > 0)
                {
                    int succ = prsWithBuilds.Sum(p => p.BuildMetrics!.SucceededCount);
                    int fail = prsWithBuilds.Sum(p => p.BuildMetrics!.FailedCount);
                    int partial = prsWithBuilds.Sum(p => p.BuildMetrics!.PartiallySucceededCount);
                    int term = succ + fail + partial;
                    ciSuccessRate = term > 0 ? (double)succ / term : null;
                }
                var avgCiWaitTimes = prsWithBuilds
                    .Where(p => p.BuildMetrics!.AvgQueueTime.HasValue)
                    .Select(p => p.BuildMetrics!.AvgQueueTime!.Value)
                    .ToList();

                return new
                {
                    Name = g.Key,
                    TotalPrs = prs.Count,
                    CompletedPrs = completed.Count,
                    AvgCycleTime = cycleTimes.Count > 0
                        ? (TimeSpan?)TimeSpan.FromTicks((long)cycleTimes.Average(t => t.Ticks))
                        : null,
                    MedianCycleTime = cycleTimes.Count > 0
                        ? (TimeSpan?)Median(cycleTimes)
                        : null,
                    AvgTimeToFirstComment = firstCommentTimes.Count > 0
                        ? (TimeSpan?)TimeSpan.FromTicks((long)firstCommentTimes.Average(t => t.Ticks))
                        : null,
                    AvgTimeToFirstApproval = firstApprovalTimes.Count > 0
                        ? (TimeSpan?)TimeSpan.FromTicks((long)firstApprovalTimes.Average(t => t.Ticks))
                        : null,
                    AvgFilesChanged = prs.Count > 0 ? prs.Average(p => p.FilesChanged) : 0,
                    ReviewsGiven = teamMetrics.ReviewsPerPerson.TryGetValue(g.Key, out var rc) ? rc : 0,
                    CommentsGiven = teamMetrics.CommentsPerPerson.TryGetValue(g.Key, out var cc) ? cc : 0,
                    FirstTimeApprovalCount = completed.Count(p => p.IsFirstTimeApproval),
                    AvgResets = completed.Count > 0 ? completed.Average(p => p.ApprovalResetCount) : 0,
                    AvgComments = prs.Count > 0 ? prs.Average(p => p.HumanCommentCount) : 0,
                    Repos = prs.Select(p => p.RepositoryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList(),
                    AvgBuilds = avgBuilds,
                    CiSuccessRate = ciSuccessRate,
                    AvgCiWait = avgCiWaitTimes.Count > 0
                        ? (TimeSpan?)TimeSpan.FromTicks((long)avgCiWaitTimes.Average(t => t.Ticks))
                        : null,
                };
            })
            .OrderByDescending(a => a.TotalPrs)
            .ToList();

        if (authors.Count == 0) return;

        var showRepos = teamMetrics.PerRepositoryBreakdown.Count > 1;

        sb.AppendLine("<div class=\"section\" id=\"contributor-table\"><h2>Individual Contributor Summary</h2>");
        sb.AppendLine("<div style=\"overflow-x:auto;background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:0.5rem;\">");
        sb.AppendLine("<table class=\"contributor-table\" id=\"contrib-tbl\">");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Contributor</th>");
        sb.AppendLine("<th>PRs</th>");
        sb.AppendLine("<th>Completed</th>");
        if (showRepos) sb.AppendLine("<th>Repos</th>");
        sb.AppendLine("<th>Avg Cycle</th>");
        sb.AppendLine("<th>Median Cycle</th>");
        sb.AppendLine("<th>Avg 1st Comment</th>");
        sb.AppendLine("<th>Avg 1st Approval</th>");
        sb.AppendLine("<th>Avg Files</th>");
        sb.AppendLine("<th>Avg Comments</th>");
        sb.AppendLine("<th>Reviews Given</th>");
        sb.AppendLine("<th>Comments Given</th>");
        sb.AppendLine("<th>First-Time Approval</th>");
        sb.AppendLine("<th>Avg Resets</th>");
        if (hasBuilds)
        {
            sb.AppendLine("<th>Avg Builds</th>");
            sb.AppendLine("<th>CI Success Rate</th>");
            sb.AppendLine("<th>Avg CI Wait</th>");
        }
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var a in authors)
        {
            var ftaRate = a.CompletedPrs > 0 ? (double)a.FirstTimeApprovalCount / a.CompletedPrs : 0;
            var ftaClass = ftaRate >= 0.70 ? "good" : ftaRate >= 0.50 ? "warn" : "bad";

            sb.AppendLine("<tr>");
            sb.Append("<td>").Append(Encode(a.Name)).AppendLine("</td>");
            sb.Append("<td>").Append(a.TotalPrs).AppendLine("</td>");
            sb.Append("<td>").Append(a.CompletedPrs).AppendLine("</td>");
            if (showRepos)
                sb.Append("<td style=\"text-align:left\">").Append(Encode(string.Join(", ", a.Repos))).AppendLine("</td>");
            sb.Append("<td>").Append(a.AvgCycleTime.HasValue ? FormatTimeSpan(a.AvgCycleTime.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(a.MedianCycleTime.HasValue ? FormatTimeSpan(a.MedianCycleTime.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(a.AvgTimeToFirstComment.HasValue ? FormatTimeSpan(a.AvgTimeToFirstComment.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(a.AvgTimeToFirstApproval.HasValue ? FormatTimeSpan(a.AvgTimeToFirstApproval.Value) : "—").AppendLine("</td>");
            sb.Append("<td>").Append(a.AvgFilesChanged.ToString("F1")).AppendLine("</td>");
            sb.Append("<td>").Append(a.AvgComments.ToString("F1")).AppendLine("</td>");
            sb.Append("<td>").Append(a.ReviewsGiven).AppendLine("</td>");
            sb.Append("<td>").Append(a.CommentsGiven).AppendLine("</td>");

            sb.Append("<td class=\"").Append(ftaClass).Append("\">");
            if (a.CompletedPrs > 0)
                sb.Append(a.FirstTimeApprovalCount).Append(" (").Append(ftaRate.ToString("P0")).Append(')');
            else
                sb.Append('—');
            sb.AppendLine("</td>");

            sb.Append("<td>").Append(a.CompletedPrs > 0 ? a.AvgResets.ToString("F1") : "—").AppendLine("</td>");

            if (hasBuilds)
            {
                sb.Append("<td>").Append(a.AvgBuilds.HasValue ? a.AvgBuilds.Value.ToString("F1") : "—").AppendLine("</td>");

                if (a.CiSuccessRate.HasValue)
                {
                    var ciClass = a.CiSuccessRate.Value >= 0.90 ? "good" : a.CiSuccessRate.Value >= 0.75 ? "warn" : "bad";
                    sb.Append("<td class=\"").Append(ciClass).Append("\">").Append(a.CiSuccessRate.Value.ToString("P0")).AppendLine("</td>");
                }
                else
                {
                    sb.Append("<td>—</td>").AppendLine();
                }

                sb.Append("<td>").Append(a.AvgCiWait.HasValue ? FormatTimeSpan(a.AvgCiWait.Value) : "—").AppendLine("</td>");
            }

            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div>");

        // Client-side sorting
        sb.AppendLine("""
<script>
(function() {
    var tbl = document.getElementById('contrib-tbl');
    var headers = tbl.querySelectorAll('th');
    headers.forEach(function(th, idx) {
        th.addEventListener('click', function() {
            var rows = Array.from(tbl.querySelectorAll('tbody tr'));
            var asc = th.dataset.asc === '1';
            rows.sort(function(a, b) {
                var aVal = a.children[idx].textContent.trim();
                var bVal = b.children[idx].textContent.trim();
                var aNum = parseFloat(aVal.replace(/[^\d.\-]/g, ''));
                var bNum = parseFloat(bVal.replace(/[^\d.\-]/g, ''));
                if (!isNaN(aNum) && !isNaN(bNum)) return asc ? aNum - bNum : bNum - aNum;
                return asc ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
            });
            rows.forEach(function(r) { tbl.querySelector('tbody').appendChild(r); });
            th.dataset.asc = asc ? '0' : '1';
        });
    });
})();
</script>
""");
        sb.AppendLine("</div>");
    }

    private static TimeSpan Median(List<TimeSpan> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
            return TimeSpan.FromTicks((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2);
        return sorted[mid];
    }

    private static void AppendStatusDistribution(StringBuilder sb, TeamMetrics team)
    {
        sb.AppendLine("<div class=\"section\" id=\"status\"><h2>PR Status Distribution</h2><div class=\"chart-grid\">");
        sb.AppendLine("<div class=\"chart-container\" id=\"chart-status-0\"></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<script>");
        sb.Append("var statusData = [{values: [")
          .Append(team.CompletedPrCount).Append(',')
          .Append(team.AbandonedPrCount).Append(',')
          .Append(team.ActivePrCount)
          .Append("], labels: ['Completed', 'Abandoned', 'Active'], type: 'pie', ")
          .Append("marker: {colors: ['#10b981', '#ef4444', '#3b82f6']}, hole: 0.4}];");
        sb.Append("var statusLayout = {title: 'PR Status Distribution', ")
          .Append("paper_bgcolor: '").Append(DarkBg).Append("', ")
          .Append("plot_bgcolor: '").Append(DarkPlotBg).Append("', ")
          .Append("font: {color: '").Append(DarkText).Append("'}};");
        sb.AppendLine("Plotly.newPlot('chart-status-0', statusData, statusLayout, {responsive: true});");
        sb.AppendLine("</script>");
        sb.AppendLine("</div>");
    }

    private static void AppendActiveAgeSummary(
        StringBuilder sb, List<PullRequestMetrics> metrics)
    {
        var active = metrics
            .Where(m => m.Status == PrStatus.Active && m.ActiveAge.HasValue)
            .OrderByDescending(m => m.ActiveAge!.Value)
            .ToList();

        if (active.Count == 0) return;

        sb.AppendLine("<div class=\"section\" id=\"active-age\"><h2>Active PR Age Distribution</h2><div class=\"chart-grid\">");
        sb.AppendLine("<div class=\"chart-container\" id=\"chart-active-age-0\"></div>");
        sb.AppendLine("</div>");

        var ages = string.Join(",", active.Select(m => m.ActiveAge!.Value.TotalDays.ToString("F1")));
        var titles = string.Join(",", active.Select(m => "'" + EscapeJs(m.Title) + "'"));

        sb.AppendLine("<script>");
        sb.Append("var ageData = [{x: [").Append(ages)
          .Append("], text: [").Append(titles)
          .Append("], type: 'histogram', marker: {color: '#3b82f6'}}];");
        sb.Append("var ageLayout = {title: 'Active PR Age Distribution', ")
          .Append("xaxis: {title: 'Days Open', gridcolor: '").Append(DarkGrid).Append("'}, ")
          .Append("yaxis: {title: 'Number of PRs', gridcolor: '").Append(DarkGrid).Append("'}, ")
          .Append("paper_bgcolor: '").Append(DarkBg).Append("', ")
          .Append("plot_bgcolor: '").Append(DarkPlotBg).Append("', ")
          .Append("font: {color: '").Append(DarkText).Append("'}};");
        sb.AppendLine("Plotly.newPlot('chart-active-age-0', ageData, ageLayout, {responsive: true});");
        sb.AppendLine("</script>");
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

    private static string EscapeJs(string text)
    {
        return text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
    }
}
