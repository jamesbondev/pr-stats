using FluentAssertions;
using PrStats.Models;
using PrStats.Visualization;

namespace PrStats.Tests.Visualization;

public class ComparisonDashboardGeneratorTests
{
    private static TeamComparisonEntry CreateTeamEntry(
        string label = "Team A",
        int completedPrCount = 5,
        double prsPerWeek = 2.0,
        int uniqueContributors = 3,
        List<PullRequestMetrics>? metrics = null,
        PercentileMetrics? percentiles = null)
    {
        return new TeamComparisonEntry
        {
            Label = label,
            Report = new PrStatsReport
            {
                SchemaVersion = PrStatsReport.CurrentSchemaVersion,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Organization = "https://dev.azure.com/testorg",
                Project = "TestProject",
                RepositoryDisplayName = label,
                Days = 90,
                PullRequests = [],
                Metrics = metrics ?? CreateDefaultMetrics(),
                TeamMetrics = new TeamMetricsSummary
                {
                    TotalPrCount = completedPrCount + 2,
                    CompletedPrCount = completedPrCount,
                    AbandonedPrCount = 1,
                    ActivePrCount = 1,
                    AvgCycleTime = TimeSpan.FromHours(30),
                    MedianCycleTime = TimeSpan.FromHours(24),
                    AvgTimeToFirstComment = TimeSpan.FromHours(2),
                    AvgTimeToFirstApproval = TimeSpan.FromHours(4),
                    AvgFilesChanged = 5.0,
                    AvgCommitsPerPr = 3.0,
                    AbandonedRate = 0.05,
                    FirstTimeApprovalRate = 0.80,
                    ApprovalResetRate = 0.10,
                    ThreadResolutionRate = 0.90,
                    ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>(),
                    ReviewsPerPerson = new Dictionary<string, int>(),
                    CommentsPerPerson = new Dictionary<string, int>(),
                    PrsPerAuthor = new Dictionary<string, int>(),
                    PairingMatrix = [],
                    PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>(),
                },
            },
            Percentiles = percentiles ?? new PercentileMetrics
            {
                MedianCycleTime = TimeSpan.FromHours(24),
                P75CycleTime = TimeSpan.FromHours(48),
                MedianTimeToFirstComment = TimeSpan.FromHours(2),
                P75TimeToFirstComment = TimeSpan.FromHours(4),
                MedianTimeToFirstApproval = TimeSpan.FromHours(3),
                P75TimeToFirstApproval = TimeSpan.FromHours(6),
            },
            PrsPerWeek = prsPerWeek,
            UniqueContributorCount = uniqueContributors,
        };
    }

    private static List<PullRequestMetrics> CreateDefaultMetrics()
    {
        return
        [
            new PullRequestMetrics
            {
                PullRequestId = 1,
                Title = "PR #1",
                RepositoryName = "test-repo",
                Status = PrStatus.Completed,
                IsDraft = false,
                AuthorDisplayName = "Alice",
                CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                ClosedDate = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc),
                TotalCycleTime = TimeSpan.FromHours(24),
                TimeToFirstHumanComment = TimeSpan.FromHours(1),
                TimeToFirstApproval = TimeSpan.FromHours(2),
                FilesChanged = 5,
                CommitCount = 3,
                IterationCount = 1,
                ActiveReviewerCount = 1,
                ActiveReviewers = ["Bob"],
            },
        ];
    }

    [Fact]
    public void Generate_WithTwoTeams_ContainsTeamLabels()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(label: "Alpha Squad"),
            CreateTeamEntry(label: "Beta Team"),
        };

        var html = ComparisonDashboardGenerator.Generate(teams);

        html.Should().Contain("Alpha Squad");
        html.Should().Contain("Beta Team");
    }

    [Fact]
    public void Generate_WithTwoTeams_ContainsBenchmarkBadges()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(),
            CreateTeamEntry(label: "Team B"),
        };

        var html = ComparisonDashboardGenerator.Generate(teams);

        html.Should().Contain("class=\"badge\"");
        // Should contain at least one tier label
        html.Should().ContainAny("Elite", "Good", "Fair", "Needs Focus");
    }

    [Fact]
    public void Generate_WithTwoTeams_ContainsChartDivs()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(),
            CreateTeamEntry(label: "Team B"),
        };

        var html = ComparisonDashboardGenerator.Generate(teams);

        html.Should().Contain("id=\"cycle-compare\"");
        html.Should().Contain("id=\"review-compare\"");
        html.Should().Contain("id=\"quality-compare\"");
        html.Should().Contain("id=\"throughput-compare\"");
    }

    [Fact]
    public void Generate_WithTwoTeams_ContainsPlotlyScript()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(),
            CreateTeamEntry(label: "Team B"),
        };

        var html = ComparisonDashboardGenerator.Generate(teams);

        html.Should().Contain("Plotly.newPlot");
        html.Should().Contain("plotly-2.35.2.min.js");
    }

    [Fact]
    public void Generate_WithEmptyMetrics_DoesNotThrow()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(metrics: [], percentiles: new PercentileMetrics()),
            CreateTeamEntry(label: "Team B", metrics: [], percentiles: new PercentileMetrics()),
        };

        var act = () => ComparisonDashboardGenerator.Generate(teams);

        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_WithOneTeamEmpty_StillRendersOtherTeam()
    {
        var teams = new List<TeamComparisonEntry>
        {
            CreateTeamEntry(label: "Full Team"),
            CreateTeamEntry(label: "Empty Team", metrics: [], completedPrCount: 0,
                percentiles: new PercentileMetrics()),
        };

        var html = ComparisonDashboardGenerator.Generate(teams);

        html.Should().Contain("Full Team");
        html.Should().Contain("Empty Team");
        // The full team's data should still appear
        html.Should().Contain("Comparison Table");
    }
}
