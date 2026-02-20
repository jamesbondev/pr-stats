using System.Text.Json;
using System.Text.Json.Serialization;
using PrStats.Configuration;
using PrStats.Models;

namespace PrStats.Services;

public static class ReportExporter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task ExportJsonAsync(
        string jsonPath,
        AppSettings settings,
        List<PullRequestData> prs,
        List<PullRequestMetrics> metrics,
        TeamMetrics teamMetrics)
    {
        var report = new PrStatsReport
        {
            SchemaVersion = PrStatsReport.CurrentSchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Organization = settings.Organization,
            Project = settings.Project,
            RepositoryDisplayName = settings.RepositoryDisplayName,
            Days = settings.Days,
            PullRequests = prs,
            Metrics = metrics,
            TeamMetrics = ConvertTeamMetrics(teamMetrics),
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(jsonPath, json);
    }

    internal static TeamMetricsSummary ConvertTeamMetrics(TeamMetrics teamMetrics)
    {
        var pairingEntries = teamMetrics.PairingMatrix
            .Select(kvp => new PairingEntry
            {
                Author = kvp.Key.Author,
                Reviewer = kvp.Key.Reviewer,
                Count = kvp.Value,
            })
            .ToList();

        return new TeamMetricsSummary
        {
            TotalPrCount = teamMetrics.TotalPrCount,
            CompletedPrCount = teamMetrics.CompletedPrCount,
            AbandonedPrCount = teamMetrics.AbandonedPrCount,
            ActivePrCount = teamMetrics.ActivePrCount,
            AvgCycleTime = teamMetrics.AvgCycleTime,
            MedianCycleTime = teamMetrics.MedianCycleTime,
            AvgTimeToFirstComment = teamMetrics.AvgTimeToFirstComment,
            AvgTimeToFirstApproval = teamMetrics.AvgTimeToFirstApproval,
            AvgFilesChanged = teamMetrics.AvgFilesChanged,
            AvgCommitsPerPr = teamMetrics.AvgCommitsPerPr,
            AbandonedRate = teamMetrics.AbandonedRate,
            FirstTimeApprovalRate = teamMetrics.FirstTimeApprovalRate,
            ThreadResolutionRate = teamMetrics.ThreadResolutionRate,
            ThroughputByAuthor = teamMetrics.ThroughputByAuthor,
            ReviewsPerPerson = teamMetrics.ReviewsPerPerson,
            PrsPerAuthor = teamMetrics.PrsPerAuthor,
            PairingMatrix = pairingEntries,
            PerRepositoryBreakdown = teamMetrics.PerRepositoryBreakdown,
        };
    }
}
