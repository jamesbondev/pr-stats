using FluentAssertions;
using PrStats.Models;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class ComparisonAnalyzerTests
{
    private static PrStatsReport CreateReport(
        int days = 90,
        int completedPrCount = 10,
        string repoName = "test-repo",
        List<PullRequestMetrics>? metrics = null,
        TeamMetricsSummary? teamMetrics = null)
    {
        return new PrStatsReport
        {
            SchemaVersion = PrStatsReport.CurrentSchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Organization = "https://dev.azure.com/testorg",
            Project = "TestProject",
            RepositoryDisplayName = repoName,
            Days = days,
            PullRequests = [],
            Metrics = metrics ?? [],
            TeamMetrics = teamMetrics ?? CreateTeamMetricsSummary(completedPrCount),
        };
    }

    private static TeamMetricsSummary CreateTeamMetricsSummary(
        int completedPrCount = 10,
        double firstTimeApprovalRate = 0.80,
        double abandonedRate = 0.05,
        double approvalResetRate = 0.10,
        double threadResolutionRate = 0.90,
        double avgFilesChanged = 5.0)
    {
        return new TeamMetricsSummary
        {
            TotalPrCount = completedPrCount + 2,
            CompletedPrCount = completedPrCount,
            AbandonedPrCount = 1,
            ActivePrCount = 1,
            AvgCycleTime = TimeSpan.FromHours(24),
            MedianCycleTime = TimeSpan.FromHours(20),
            AvgTimeToFirstComment = TimeSpan.FromHours(2),
            AvgTimeToFirstApproval = TimeSpan.FromHours(4),
            AvgFilesChanged = avgFilesChanged,
            AvgCommitsPerPr = 3.0,
            AbandonedRate = abandonedRate,
            FirstTimeApprovalRate = firstTimeApprovalRate,
            ApprovalResetRate = approvalResetRate,
            ThreadResolutionRate = threadResolutionRate,
            ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>(),
            ReviewsPerPerson = new Dictionary<string, int>(),
            CommentsPerPerson = new Dictionary<string, int>(),
            PrsPerAuthor = new Dictionary<string, int>(),
            PairingMatrix = [],
            PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>(),
        };
    }

    private static PullRequestMetrics CreateMetrics(
        int id = 1,
        PrStatus status = PrStatus.Completed,
        bool isDraft = false,
        string authorName = "Alice",
        TimeSpan? cycleTime = null,
        TimeSpan? firstComment = null,
        TimeSpan? firstApproval = null)
    {
        return new PullRequestMetrics
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = "test-repo",
            Status = status,
            IsDraft = isDraft,
            AuthorDisplayName = authorName,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedDate = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            TotalCycleTime = cycleTime ?? TimeSpan.FromHours(24),
            TimeToFirstHumanComment = firstComment ?? TimeSpan.FromHours(1),
            TimeToFirstApproval = firstApproval ?? TimeSpan.FromHours(2),
            TimeFromApprovalToMerge = TimeSpan.FromHours(22),
            FilesChanged = 5,
            CommitCount = 3,
            IterationCount = 1,
            HumanCommentCount = 1,
            IsFirstTimeApproval = true,
            ResolvableThreadCount = 1,
            ResolvedThreadCount = 1,
            ActiveReviewerCount = 1,
            ActiveReviewers = ["Bob"],
        };
    }

    // Percentile tests

    [Fact]
    public void Percentile_EmptyList_ReturnsNull()
    {
        var result = ComparisonAnalyzer.Percentile([], 0.50);
        result.Should().BeNull();
    }

    [Fact]
    public void Percentile_SingleElement_ReturnsThatElement()
    {
        var values = new List<TimeSpan> { TimeSpan.FromHours(10) };
        var result = ComparisonAnalyzer.Percentile(values, 0.50);
        result.Should().Be(TimeSpan.FromHours(10));
    }

    [Fact]
    public void Percentile_P75_WithKnownValues_MatchesExpected()
    {
        // Values: 1h, 2h, 3h, 4h => p75 index = 3 * 0.75 = 2.25 => lerp(3h, 4h, 0.25) = 3.25h
        var values = new List<TimeSpan>
        {
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(4),
        };

        var result = ComparisonAnalyzer.Percentile(values, 0.75);
        result.Should().BeCloseTo(TimeSpan.FromHours(3.25), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Percentile_P50_MatchesMedian()
    {
        // Values: 10h, 20h, 30h => p50 index = 2 * 0.5 = 1.0 => 20h
        var values = new List<TimeSpan>
        {
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(20),
            TimeSpan.FromHours(30),
        };

        var result = ComparisonAnalyzer.Percentile(values, 0.50);
        result.Should().Be(TimeSpan.FromHours(20));
    }

    [Fact]
    public void Percentile_UnsortedInput_StillReturnsCorrectResult()
    {
        var values = new List<TimeSpan>
        {
            TimeSpan.FromHours(30),
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(20),
        };

        var result = ComparisonAnalyzer.Percentile(values, 0.50);
        result.Should().Be(TimeSpan.FromHours(20));
    }

    // Analyze tests

    [Fact]
    public void Analyze_WithTwoReports_ReturnsCorrectLabels()
    {
        var reports = new List<PrStatsReport>
        {
            CreateReport(repoName: "repo-a"),
            CreateReport(repoName: "repo-b"),
        };

        var result = ComparisonAnalyzer.Analyze(reports, ["Team A", "Team B"]);

        result.Should().HaveCount(2);
        result[0].Label.Should().Be("Team A");
        result[1].Label.Should().Be("Team B");
    }

    [Fact]
    public void Analyze_CalculatesPrsPerWeekCorrectly()
    {
        // 14 completed PRs in 90 days = 14 / (90/7) = 14 / 12.857 ≈ 1.089
        var report = CreateReport(days: 90, completedPrCount: 14);
        var result = ComparisonAnalyzer.Analyze([report], ["Team"]);

        result[0].PrsPerWeek.Should().BeApproximately(14.0 / (90.0 / 7.0), 0.001);
    }

    [Fact]
    public void Analyze_CalculatesUniqueContributors()
    {
        var metrics = new List<PullRequestMetrics>
        {
            CreateMetrics(id: 1, authorName: "Alice"),
            CreateMetrics(id: 2, authorName: "Bob"),
            CreateMetrics(id: 3, authorName: "Alice"),
        };

        var report = CreateReport(metrics: metrics);
        var result = ComparisonAnalyzer.Analyze([report], ["Team"]);

        result[0].UniqueContributorCount.Should().Be(2);
    }

    [Fact]
    public void Analyze_WithNoCompletedPrs_HandlesGracefully()
    {
        var metrics = new List<PullRequestMetrics>
        {
            CreateMetrics(id: 1, status: PrStatus.Active, cycleTime: null),
        };

        var report = CreateReport(completedPrCount: 0, metrics: metrics);
        var result = ComparisonAnalyzer.Analyze([report], ["Team"]);

        result[0].Percentiles.MedianCycleTime.Should().BeNull();
        result[0].Percentiles.P75CycleTime.Should().BeNull();
    }

    [Fact]
    public void Analyze_WithDraftPrs_ExcludesFromPercentiles()
    {
        var metrics = new List<PullRequestMetrics>
        {
            CreateMetrics(id: 1, isDraft: false, cycleTime: TimeSpan.FromHours(10)),
            CreateMetrics(id: 2, isDraft: true, cycleTime: TimeSpan.FromHours(100)),
            CreateMetrics(id: 3, isDraft: false, cycleTime: TimeSpan.FromHours(20)),
        };

        var report = CreateReport(metrics: metrics);
        var result = ComparisonAnalyzer.Analyze([report], ["Team"]);

        // Only non-draft completed PRs (10h, 20h) should be used
        result[0].Percentiles.MedianCycleTime.Should().BeCloseTo(
            TimeSpan.FromHours(15), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Analyze_UsesTeamMetricsSummaryForRates()
    {
        var teamMetrics = CreateTeamMetricsSummary(
            firstTimeApprovalRate: 0.75,
            abandonedRate: 0.12,
            approvalResetRate: 0.08,
            threadResolutionRate: 0.95);

        var report = CreateReport(teamMetrics: teamMetrics);
        var result = ComparisonAnalyzer.Analyze([report], ["Team"]);

        // Rates should come directly from TeamMetricsSummary, not recalculated
        result[0].Report.TeamMetrics.FirstTimeApprovalRate.Should().Be(0.75);
        result[0].Report.TeamMetrics.AbandonedRate.Should().Be(0.12);
        result[0].Report.TeamMetrics.ApprovalResetRate.Should().Be(0.08);
        result[0].Report.TeamMetrics.ThreadResolutionRate.Should().Be(0.95);
    }
}
