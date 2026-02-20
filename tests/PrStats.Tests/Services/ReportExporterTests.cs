using System.Text.Json;
using FluentAssertions;
using PrStats.Models;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class ReportExporterTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static PullRequestData CreatePr(
        int id = 1,
        PrStatus status = PrStatus.Completed,
        string authorName = "Alice",
        string authorId = "a1",
        string repo = "test-repo",
        DateTime? creationDate = null,
        DateTime? closedDate = null)
    {
        var created = creationDate ?? new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        return new PullRequestData
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = repo,
            Status = status,
            IsDraft = false,
            CreationDate = created,
            ClosedDate = closedDate ?? (status == PrStatus.Completed ? created.AddHours(24) : null),
            AuthorDisplayName = authorName,
            AuthorId = authorId,
            Reviewers =
            [
                new ReviewerInfo
                {
                    DisplayName = "Bob", Id = "b1", Vote = 10,
                    IsContainer = false, IsRequired = true,
                },
            ],
            Threads =
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "text",
                    PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Bob", AuthorId = "b1",
                    IsAuthorBot = false, Status = "fixed", CommentCount = 2,
                    IsVoteUpdate = false,
                },
            ],
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
        PrStatus status = PrStatus.Completed,
        string authorName = "Alice",
        string repo = "test-repo",
        TimeSpan? cycleTime = null)
    {
        return new PullRequestMetrics
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = repo,
            Status = status,
            IsDraft = false,
            AuthorDisplayName = authorName,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedDate = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            TotalCycleTime = cycleTime ?? TimeSpan.FromHours(24),
            TimeToFirstHumanComment = TimeSpan.FromHours(1),
            TimeToFirstApproval = TimeSpan.FromHours(2),
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

    private static TeamMetrics CreateTeamMetrics()
    {
        return new TeamMetrics
        {
            TotalPrCount = 2,
            CompletedPrCount = 2,
            AbandonedPrCount = 0,
            ActivePrCount = 0,
            AvgCycleTime = TimeSpan.FromHours(24),
            MedianCycleTime = TimeSpan.FromHours(24),
            AvgTimeToFirstComment = TimeSpan.FromHours(1),
            AvgTimeToFirstApproval = TimeSpan.FromHours(2),
            AvgFilesChanged = 5.0,
            AvgCommitsPerPr = 3.0,
            AbandonedRate = 0.0,
            FirstTimeApprovalRate = 1.0,
            ThreadResolutionRate = 1.0,
            ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>
            {
                ["Alice"] = [new WeeklyCount { WeekStart = new DateTime(2025, 1, 1), Count = 2 }],
            },
            ReviewsPerPerson = new Dictionary<string, int> { ["Bob"] = 2 },
            PrsPerAuthor = new Dictionary<string, int> { ["Alice"] = 2 },
            PairingMatrix = new Dictionary<ReviewerAuthorPair, int>
            {
                [new ReviewerAuthorPair("Alice", "Bob")] = 2,
            },
            PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>
            {
                ["test-repo"] = new RepositoryBreakdown
                {
                    TotalPrCount = 2,
                    CompletedPrCount = 2,
                    AbandonedPrCount = 0,
                    ActivePrCount = 0,
                    AbandonedRate = 0.0,
                    AvgCycleTime = TimeSpan.FromHours(24),
                    MedianCycleTime = TimeSpan.FromHours(24),
                    AvgFilesChanged = 5.0,
                    FirstTimeApprovalRate = 1.0,
                },
            },
        };
    }

    private static Configuration.AppSettings CreateSettings() => new()
    {
        Organization = "https://dev.azure.com/testorg",
        Project = "TestProject",
        Pat = "fake-pat",
    };

    [Fact]
    public async Task ExportAndDeserialize_RoundTrips_AllFields()
    {
        var prs = new List<PullRequestData> { CreatePr(1), CreatePr(2) };
        var metrics = new List<PullRequestMetrics> { CreateMetrics(1), CreateMetrics(2) };
        var teamMetrics = CreateTeamMetrics();

        await ReportExporter.ExportJsonAsync(_tempFile, CreateSettings(), prs, metrics, teamMetrics);

        var json = await File.ReadAllTextAsync(_tempFile);
        var report = JsonSerializer.Deserialize<PrStatsReport>(json, ReportExporter.JsonOptions)!;

        report.SchemaVersion.Should().Be(PrStatsReport.CurrentSchemaVersion);
        report.Organization.Should().Be("https://dev.azure.com/testorg");
        report.Project.Should().Be("TestProject");
        report.RepositoryDisplayName.Should().Be("All Repositories");
        report.PullRequests.Should().HaveCount(2);
        report.Metrics.Should().HaveCount(2);
        report.TeamMetrics.TotalPrCount.Should().Be(2);
        report.TeamMetrics.CompletedPrCount.Should().Be(2);
        report.TeamMetrics.AvgCycleTime.Should().Be(TimeSpan.FromHours(24));
        report.TeamMetrics.ThroughputByAuthor.Should().ContainKey("Alice");
        report.TeamMetrics.ReviewsPerPerson.Should().ContainKey("Bob");
        report.TeamMetrics.PrsPerAuthor.Should().ContainKey("Alice");
        report.TeamMetrics.PerRepositoryBreakdown.Should().ContainKey("test-repo");
    }

    [Fact]
    public async Task ExportAndDeserialize_PairingMatrix_ConvertsCorrectly()
    {
        var teamMetrics = new TeamMetrics
        {
            TotalPrCount = 3,
            CompletedPrCount = 3,
            AbandonedPrCount = 0,
            ActivePrCount = 0,
            ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>(),
            ReviewsPerPerson = new Dictionary<string, int>(),
            PrsPerAuthor = new Dictionary<string, int>(),
            PairingMatrix = new Dictionary<ReviewerAuthorPair, int>
            {
                [new ReviewerAuthorPair("Alice", "Bob")] = 5,
                [new ReviewerAuthorPair("Charlie", "Bob")] = 3,
                [new ReviewerAuthorPair("Alice", "Charlie")] = 2,
            },
            PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>(),
        };

        await ReportExporter.ExportJsonAsync(_tempFile, CreateSettings(), [], [], teamMetrics);

        var json = await File.ReadAllTextAsync(_tempFile);
        var report = JsonSerializer.Deserialize<PrStatsReport>(json, ReportExporter.JsonOptions)!;

        report.TeamMetrics.PairingMatrix.Should().HaveCount(3);

        var aliceBob = report.TeamMetrics.PairingMatrix
            .First(p => p.Author == "Alice" && p.Reviewer == "Bob");
        aliceBob.Count.Should().Be(5);

        var charlieBob = report.TeamMetrics.PairingMatrix
            .First(p => p.Author == "Charlie" && p.Reviewer == "Bob");
        charlieBob.Count.Should().Be(3);

        var aliceCharlie = report.TeamMetrics.PairingMatrix
            .First(p => p.Author == "Alice" && p.Reviewer == "Charlie");
        aliceCharlie.Count.Should().Be(2);
    }

    [Fact]
    public async Task ExportAndDeserialize_EnumsAsStrings()
    {
        var prs = new List<PullRequestData> { CreatePr(1, PrStatus.Completed) };
        var metrics = new List<PullRequestMetrics> { CreateMetrics(1, PrStatus.Completed) };
        var teamMetrics = CreateTeamMetrics();

        await ReportExporter.ExportJsonAsync(_tempFile, CreateSettings(), prs, metrics, teamMetrics);

        var rawJson = await File.ReadAllTextAsync(_tempFile);

        // Verify enums are serialized as strings, not integers
        rawJson.Should().Contain("\"completed\"");
        rawJson.Should().NotContain("\"status\": 0");
        rawJson.Should().NotContain("\"status\": 1");
    }

    [Fact]
    public async Task ExportAndDeserialize_EmptyData_HandlesGracefully()
    {
        var teamMetrics = new TeamMetrics
        {
            TotalPrCount = 0,
            CompletedPrCount = 0,
            AbandonedPrCount = 0,
            ActivePrCount = 0,
            ThroughputByAuthor = new Dictionary<string, List<WeeklyCount>>(),
            ReviewsPerPerson = new Dictionary<string, int>(),
            PrsPerAuthor = new Dictionary<string, int>(),
            PairingMatrix = new Dictionary<ReviewerAuthorPair, int>(),
            PerRepositoryBreakdown = new Dictionary<string, RepositoryBreakdown>(),
        };

        await ReportExporter.ExportJsonAsync(_tempFile, CreateSettings(), [], [], teamMetrics);

        var json = await File.ReadAllTextAsync(_tempFile);
        var report = JsonSerializer.Deserialize<PrStatsReport>(json, ReportExporter.JsonOptions)!;

        report.PullRequests.Should().BeEmpty();
        report.Metrics.Should().BeEmpty();
        report.TeamMetrics.TotalPrCount.Should().Be(0);
        report.TeamMetrics.PairingMatrix.Should().BeEmpty();
        report.TeamMetrics.AvgCycleTime.Should().BeNull();
        report.TeamMetrics.AbandonedRate.Should().Be(0);
    }
}
