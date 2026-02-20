using FluentAssertions;
using PrStats.Chat;
using PrStats.Models;

namespace PrStats.Tests.Chat;

public class PrDataToolsTests
{
    private static PrStatsReport CreateReport(
        List<PullRequestData>? prs = null,
        List<PullRequestMetrics>? metrics = null,
        TeamMetricsSummary? teamMetrics = null)
    {
        return new PrStatsReport
        {
            SchemaVersion = PrStatsReport.CurrentSchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Organization = "https://dev.azure.com/testorg",
            Project = "TestProject",
            RepositoryDisplayName = "All Repositories",
            Days = 90,
            PullRequests = prs ?? [],
            Metrics = metrics ?? [],
            TeamMetrics = teamMetrics ?? CreateEmptyTeamMetrics(),
        };
    }

    private static TeamMetricsSummary CreateEmptyTeamMetrics() => new()
    {
        TotalPrCount = 0,
        CompletedPrCount = 0,
        AbandonedPrCount = 0,
        ActivePrCount = 0,
        ApprovalResetRate = 0,
        ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>(),
        ReviewsPerPerson = new Dictionary<string, int>(),
        CommentsPerPerson = new Dictionary<string, int>(),
        PrsPerAuthor = new Dictionary<string, int>(),
        PairingMatrix = [],
        PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>(),
    };

    private static PullRequestData CreatePr(
        int id = 1,
        string authorName = "Alice Smith",
        string authorId = "a1",
        string repo = "test-repo",
        PrStatus status = PrStatus.Completed)
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        return new PullRequestData
        {
            PullRequestId = id,
            Title = $"Fix bug in {repo} #{id}",
            RepositoryName = repo,
            Status = status,
            IsDraft = false,
            CreationDate = created,
            ClosedDate = status == PrStatus.Completed ? created.AddHours(24) : null,
            AuthorDisplayName = authorName,
            AuthorId = authorId,
            Reviewers =
            [
                new ReviewerInfo
                {
                    DisplayName = "Bob Jones", Id = "b1", Vote = 10,
                    IsContainer = false, IsRequired = true,
                },
            ],
            Threads = [],
            Iterations =
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "create" },
            ],
            FilesChanged = 5,
            CommitCount = 3,
        };
    }

    private static PullRequestMetrics CreateMetrics(
        int id = 1,
        string authorName = "Alice Smith",
        string repo = "test-repo",
        PrStatus status = PrStatus.Completed,
        TimeSpan? cycleTime = null)
    {
        return new PullRequestMetrics
        {
            PullRequestId = id,
            Title = $"Fix bug in {repo} #{id}",
            RepositoryName = repo,
            Status = status,
            IsDraft = false,
            AuthorDisplayName = authorName,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedDate = status == PrStatus.Completed
                ? new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc)
                : null,
            TotalCycleTime = status == PrStatus.Completed
                ? (cycleTime ?? TimeSpan.FromHours(24))
                : null,
            TimeToFirstHumanComment = TimeSpan.FromHours(1),
            TimeToFirstApproval = TimeSpan.FromHours(2),
            TimeFromApprovalToMerge = TimeSpan.FromHours(22),
            FilesChanged = 5,
            CommitCount = 3,
            IterationCount = 1,
            HumanCommentCount = 1,
            IsFirstTimeApproval = true,
            ResolvableThreadCount = 0,
            ResolvedThreadCount = 0,
            ActiveReviewerCount = 1,
            ActiveReviewers = ["Bob Jones"],
        };
    }

    [Fact]
    public async Task SearchPullRequests_ByAuthor_ReturnsMatches()
    {
        var report = CreateReport(
            prs: [CreatePr(1, "Alice Smith"), CreatePr(2, "Bob Jones", "b1")],
            metrics: [CreateMetrics(1, "Alice Smith"), CreateMetrics(2, "Bob Jones")]);

        var tools = new PrDataTools(report);
        var result = await tools.SearchPullRequests(author: "alice");

        result.Should().Contain("Alice Smith");
        result.Should().NotContain("Bob Jones");
    }

    [Fact]
    public async Task SearchPullRequests_ByCycleTimeRange_FiltersCorrectly()
    {
        var report = CreateReport(
            metrics:
            [
                CreateMetrics(1, cycleTime: TimeSpan.FromHours(10)),
                CreateMetrics(2, cycleTime: TimeSpan.FromHours(50)),
                CreateMetrics(3, cycleTime: TimeSpan.FromHours(100)),
            ]);

        var tools = new PrDataTools(report);
        var result = await tools.SearchPullRequests(minCycleTimeHours: 20, maxCycleTimeHours: 60);

        // Only PR 2 (50 hours) should be in the 20-60 hour range
        result.Should().Contain("| 2 | Fix bug");
        result.Should().Contain("Found 1 matching");
        // PRs 1 (10h) and 3 (100h) should be filtered out
        result.Should().NotContain("| 1 | Fix bug");
        result.Should().NotContain("| 3 | Fix bug");
    }

    [Fact]
    public async Task SearchPullRequests_MultipleFilters_IntersectsCorrectly()
    {
        var report = CreateReport(
            metrics:
            [
                CreateMetrics(1, "Alice Smith", "repo-a", PrStatus.Completed, TimeSpan.FromHours(50)),
                CreateMetrics(2, "Alice Smith", "repo-b", PrStatus.Completed, TimeSpan.FromHours(50)),
                CreateMetrics(3, "Bob Jones", "repo-a", PrStatus.Completed, TimeSpan.FromHours(50)),
                CreateMetrics(4, "Alice Smith", "repo-a", PrStatus.Active),
            ]);

        var tools = new PrDataTools(report);
        var result = await tools.SearchPullRequests(
            author: "Alice", repo: "repo-a", status: "completed");

        result.Should().Contain("| 1 |");
        result.Should().NotContain("| 2 |");
        result.Should().NotContain("| 3 |");
        result.Should().NotContain("| 4 |");
    }

    [Fact]
    public async Task SearchPullRequests_NoMatches_ReturnsEmptyMessage()
    {
        var report = CreateReport(
            metrics: [CreateMetrics(1, "Alice Smith")]);

        var tools = new PrDataTools(report);
        var result = await tools.SearchPullRequests(author: "Nonexistent");

        result.Should().Contain("No pull requests match");
    }

    [Fact]
    public async Task SearchPullRequests_MaxResults_CapsOutput()
    {
        var metrics = Enumerable.Range(1, 30)
            .Select(i => CreateMetrics(i, "Alice Smith"))
            .ToList();

        var report = CreateReport(metrics: metrics);

        var tools = new PrDataTools(report);
        var result = await tools.SearchPullRequests(maxResults: 5);

        // Should only have 5 data rows (plus header rows)
        var dataRows = result.Split('\n')
            .Count(line => line.StartsWith("| ") && !line.StartsWith("| PR ID") && !line.StartsWith("|---"));
        dataRows.Should().Be(5);
    }

    [Fact]
    public async Task GetPullRequestDetail_ExistingPr_ReturnsFullDetail()
    {
        var report = CreateReport(
            prs: [CreatePr(42, "Alice Smith")],
            metrics: [CreateMetrics(42, "Alice Smith")]);

        var tools = new PrDataTools(report);
        var result = await tools.GetPullRequestDetail(42);

        result.Should().Contain("PR #42");
        result.Should().Contain("Alice Smith");
        result.Should().Contain("Bob Jones");
        result.Should().Contain("Approved");
        result.Should().Contain("test-repo");
    }

    [Fact]
    public async Task GetPullRequestDetail_NonExistentPr_ReturnsNotFound()
    {
        var report = CreateReport(
            prs: [CreatePr(1)],
            metrics: [CreateMetrics(1)]);

        var tools = new PrDataTools(report);
        var result = await tools.GetPullRequestDetail(999);

        result.Should().Contain("999");
        result.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSlowestPullRequests_ReturnsOrderedByCycleTime()
    {
        var report = CreateReport(
            metrics:
            [
                CreateMetrics(1, cycleTime: TimeSpan.FromHours(10)),
                CreateMetrics(2, cycleTime: TimeSpan.FromHours(100)),
                CreateMetrics(3, cycleTime: TimeSpan.FromHours(50)),
            ]);

        var tools = new PrDataTools(report);
        var result = await tools.GetSlowestPullRequests(count: 3);

        var lines = result.Split('\n')
            .Where(l => l.StartsWith("| ") && !l.StartsWith("| Rank") && !l.StartsWith("|---"))
            .ToList();

        lines.Should().HaveCount(3);
        // First row (rank 1) should be PR 2 (100 hours)
        lines[0].Should().Contain("| 2 |");
        // Second row (rank 2) should be PR 3 (50 hours)
        lines[1].Should().Contain("| 3 |");
        // Third row (rank 3) should be PR 1 (10 hours)
        lines[2].Should().Contain("| 1 |");
    }

    [Fact]
    public async Task GetAuthorStats_PartialMatch_FindsAuthor()
    {
        var report = CreateReport(
            metrics:
            [
                CreateMetrics(1, "Alice Smith"),
                CreateMetrics(2, "Alice Smith"),
                CreateMetrics(3, "Bob Jones"),
            ]);

        var tools = new PrDataTools(report);
        var result = await tools.GetAuthorStats("alice");

        result.Should().Contain("Alice Smith");
        result.Should().Contain("Total PRs: 2");
    }

    [Fact]
    public async Task GetTeamSummary_EmptyReport_HandlesGracefully()
    {
        var report = CreateReport();

        var tools = new PrDataTools(report);
        var result = await tools.GetTeamSummary();

        result.Should().Contain("Total: 0");
        result.Should().Contain("Completed: 0");
        result.Should().Contain("Abandoned rate:");
        result.Should().Contain("0.0");
    }
}
