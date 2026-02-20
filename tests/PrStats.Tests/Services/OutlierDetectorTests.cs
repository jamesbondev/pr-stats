using FluentAssertions;
using PrStats.Models;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class OutlierDetectorTests
{
    private static PullRequestMetrics CreateMetrics(
        int id = 1,
        TimeSpan? cycleTime = null,
        TimeSpan? timeToFirstComment = null,
        int filesChanged = 5,
        int iterationCount = 1,
        int humanCommentCount = 2,
        int approvalResetCount = 0,
        PrBuildMetrics? buildMetrics = null,
        PrStatus status = PrStatus.Completed,
        bool isDraft = false,
        bool isAuthorBot = false,
        string author = "Author")
    {
        return new PullRequestMetrics
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = "test-repo",
            Status = status,
            IsDraft = isDraft,
            IsAuthorBot = isAuthorBot,
            AuthorDisplayName = author,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            TotalCycleTime = cycleTime ?? TimeSpan.FromHours(24),
            TimeToFirstHumanComment = timeToFirstComment ?? TimeSpan.FromHours(2),
            FilesChanged = filesChanged,
            IterationCount = iterationCount,
            HumanCommentCount = humanCommentCount,
            ApprovalResetCount = approvalResetCount,
            ActiveReviewers = [],
            BuildMetrics = buildMetrics,
        };
    }

    [Fact]
    public void Detect_FewerThan3Prs_ReturnsEmpty()
    {
        var metrics = new List<PullRequestMetrics>
        {
            CreateMetrics(1),
            CreateMetrics(2),
        };

        var result = OutlierDetector.Detect(metrics);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_AllIdenticalMetrics_ReturnsEmpty()
    {
        var metrics = Enumerable.Range(1, 10)
            .Select(i => CreateMetrics(i))
            .ToList();

        var result = OutlierDetector.Detect(metrics);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_OneExtremeOutlier_DetectedWithCorrectLabels()
    {
        var normal = Enumerable.Range(1, 9)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20), filesChanged: 5))
            .ToList();
        var outlier = CreateMetrics(10, cycleTime: TimeSpan.FromHours(500), filesChanged: 100);
        normal.Add(outlier);

        var result = OutlierDetector.Detect(normal);

        result.Should().NotBeEmpty();
        var found = result.First();
        found.Metrics.PullRequestId.Should().Be(10);
        found.Flags.Should().Contain(f => f.Label == "Slow Cycle" && f.CssClass == "bad");
        found.Flags.Should().Contain(f => f.Label == "Large PR" && f.CssClass == "bad");
        found.CompositeScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Detect_NonCompletedPrs_ExcludedFromAnalysis()
    {
        var normal = Enumerable.Range(1, 5)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20)))
            .ToList();
        // Add extreme outlier that is active - should be ignored
        normal.Add(CreateMetrics(99, cycleTime: TimeSpan.FromHours(1000), status: PrStatus.Active));

        var result = OutlierDetector.Detect(normal);

        result.Should().NotContain(r => r.Metrics.PullRequestId == 99);
    }

    [Fact]
    public void Detect_DraftPrs_ExcludedFromAnalysis()
    {
        var normal = Enumerable.Range(1, 5)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20)))
            .ToList();
        normal.Add(CreateMetrics(99, cycleTime: TimeSpan.FromHours(1000), isDraft: true));

        var result = OutlierDetector.Detect(normal);

        result.Should().NotContain(r => r.Metrics.PullRequestId == 99);
    }

    [Fact]
    public void Detect_BotPrs_ExcludedFromAnalysis()
    {
        var normal = Enumerable.Range(1, 5)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20)))
            .ToList();
        normal.Add(CreateMetrics(99, cycleTime: TimeSpan.FromHours(1000), isAuthorBot: true));

        var result = OutlierDetector.Detect(normal);

        result.Should().NotContain(r => r.Metrics.PullRequestId == 99);
    }

    [Fact]
    public void Detect_Max10ResultsReturned()
    {
        var normal = Enumerable.Range(1, 5)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20), filesChanged: 5))
            .ToList();
        // Add 15 outliers
        for (int i = 100; i < 115; i++)
        {
            normal.Add(CreateMetrics(i, cycleTime: TimeSpan.FromHours(500 + i), filesChanged: 200));
        }

        var result = OutlierDetector.Detect(normal);

        result.Should().HaveCountLessThanOrEqualTo(10);
    }

    [Fact]
    public void Detect_RankedByCompositeScore_MultiDimensionOutlierRanksHigher()
    {
        var normal = Enumerable.Range(1, 10)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(20), filesChanged: 5, humanCommentCount: 2))
            .ToList();
        // Single-dimension outlier
        var singleOutlier = CreateMetrics(20, cycleTime: TimeSpan.FromHours(500), filesChanged: 5, humanCommentCount: 2);
        // Multi-dimension outlier
        var multiOutlier = CreateMetrics(21, cycleTime: TimeSpan.FromHours(500), filesChanged: 200, humanCommentCount: 50);
        normal.Add(singleOutlier);
        normal.Add(multiOutlier);

        var result = OutlierDetector.Detect(normal);

        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result[0].Metrics.PullRequestId.Should().Be(21, "multi-dimension outlier should rank first");
        result[0].CompositeScore.Should().BeGreaterThan(result[1].CompositeScore);
    }

    [Fact]
    public void Detect_LowValues_NotFlagged()
    {
        var normal = Enumerable.Range(1, 9)
            .Select(i => CreateMetrics(i, cycleTime: TimeSpan.FromHours(100), filesChanged: 50))
            .ToList();
        // PR with unusually LOW values — should NOT be flagged
        var fast = CreateMetrics(10, cycleTime: TimeSpan.FromMinutes(30), filesChanged: 1);
        normal.Add(fast);

        var result = OutlierDetector.Detect(normal);

        result.Should().NotContain(r => r.Metrics.PullRequestId == 10);
    }

    [Fact]
    public void Detect_BuildMetrics_IncludedWhenPresent()
    {
        var buildData = new PrBuildMetrics
        {
            TotalBuildCount = 2,
            SucceededCount = 2,
            FailedCount = 0,
            CanceledCount = 0,
            PartiallySucceededCount = 0,
            BuildSuccessRate = 1.0,
            PerPipeline = [],
        };
        var normal = Enumerable.Range(1, 9)
            .Select(i => CreateMetrics(i, buildMetrics: buildData))
            .ToList();
        var outlierBuild = new PrBuildMetrics
        {
            TotalBuildCount = 50,
            SucceededCount = 10,
            FailedCount = 30,
            CanceledCount = 0,
            PartiallySucceededCount = 10,
            BuildSuccessRate = 0.2,
            PerPipeline = [],
        };
        normal.Add(CreateMetrics(10, buildMetrics: outlierBuild));

        var result = OutlierDetector.Detect(normal);

        result.Should().NotBeEmpty();
        var found = result.First();
        found.Flags.Should().Contain(f => f.Label == "Build Failures");
        found.Flags.Should().Contain(f => f.Label == "Many Builds");
    }

    [Fact]
    public void Detect_ApprovalResets_MostlyZeroDataset_FlagsPrWithResets()
    {
        var normal = Enumerable.Range(1, 9)
            .Select(i => CreateMetrics(i, approvalResetCount: 0))
            .ToList();
        // One PR with 3 resets in a dataset where 90% have 0
        normal.Add(CreateMetrics(10, approvalResetCount: 3));

        var result = OutlierDetector.Detect(normal);

        result.Should().NotBeEmpty();
        result.Should().Contain(r => r.Metrics.PullRequestId == 10);
        var found = result.First(r => r.Metrics.PullRequestId == 10);
        found.Flags.Should().Contain(f => f.Label == "Approval Resets");
    }

    [Fact]
    public void Detect_AllZeroApprovalResets_NoBadFlags()
    {
        var metrics = Enumerable.Range(1, 10)
            .Select(i => CreateMetrics(i, approvalResetCount: 0))
            .ToList();

        var result = OutlierDetector.Detect(metrics);

        // No PR should be flagged for approval resets when all are 0 (stddev = 0 skip)
        result.SelectMany(r => r.Flags)
            .Should().NotContain(f => f.Label == "Approval Resets");
    }
}
