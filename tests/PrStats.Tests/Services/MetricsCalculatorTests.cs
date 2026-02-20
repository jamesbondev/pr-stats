using FluentAssertions;
using PrStats.Configuration;
using PrStats.Models;
using PrStats.Services;
using Spectre.Console.Testing;

namespace PrStats.Tests.Services;

public class MetricsCalculatorTests
{
    private readonly MetricsCalculator _calculator = new();

    private static PullRequestData CreateCompletedPr(
        int id = 1,
        DateTime? creationDate = null,
        DateTime? closedDate = null,
        string authorId = "author-1",
        string authorName = "Author One",
        List<ReviewerInfo>? reviewers = null,
        List<ThreadInfo>? threads = null,
        List<IterationInfo>? iterations = null,
        int filesChanged = 5,
        int commitCount = 3,
        bool isDraft = false,
        string repositoryName = "test-repo")
    {
        var created = creationDate ?? new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var closed = closedDate ?? created.AddHours(24);

        return new PullRequestData
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = repositoryName,
            Status = PrStatus.Completed,
            IsDraft = isDraft,
            CreationDate = created,
            ClosedDate = closed,
            AuthorDisplayName = authorName,
            AuthorId = authorId,
            Reviewers = reviewers ?? [],
            Threads = threads ?? [],
            Iterations = iterations ??
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "create" },
            ],
            FilesChanged = filesChanged,
            CommitCount = commitCount,
        };
    }

    [Fact]
    public void CalculatePerPR_CompletedPrWithAllData_CalculatesCycleTime()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var closed = created.AddHours(48);
        var commentTime = created.AddHours(2);
        var approvalTime = created.AddHours(6);

        var pr = CreateCompletedPr(
            creationDate: created,
            closedDate: closed,
            reviewers:
            [
                new ReviewerInfo
                {
                    DisplayName = "Reviewer One", Id = "reviewer-1",
                    Vote = 10, IsContainer = false, IsRequired = true,
                },
            ],
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "text", PublishedDate = commentTime,
                    AuthorDisplayName = "Reviewer One", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = false,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "system", PublishedDate = approvalTime,
                    AuthorDisplayName = "Reviewer One", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().Be(TimeSpan.FromHours(48));
        metrics.TimeToFirstHumanComment.Should().Be(TimeSpan.FromHours(2));
        metrics.TimeToFirstApproval.Should().Be(TimeSpan.FromHours(6));
        metrics.TimeFromApprovalToMerge.Should().Be(TimeSpan.FromHours(42));
        metrics.HumanCommentCount.Should().Be(1);
        metrics.ActiveReviewerCount.Should().Be(1);
    }

    [Fact]
    public void CalculatePerPR_PrWithZeroThreadsAndReviewers_HandlesGracefully()
    {
        var pr = CreateCompletedPr(reviewers: [], threads: []);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().NotBeNull();
        metrics.TimeToFirstHumanComment.Should().BeNull();
        metrics.TimeToFirstApproval.Should().BeNull();
        metrics.HumanCommentCount.Should().Be(0);
        metrics.ActiveReviewerCount.Should().Be(0);
    }

    [Fact]
    public void CalculatePerPR_PrWithOnlyBotComments_ZeroHumanComments()
    {
        var pr = CreateCompletedPr(
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "text",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "Azure Pipelines", AuthorId = "bot-1",
                    IsAuthorBot = true, Status = "active", CommentCount = 1,
                    IsVoteUpdate = false,
                },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.HumanCommentCount.Should().Be(0);
    }

    [Fact]
    public void CalculatePerPR_FirstTimeApproval_ApprovedBeforeSecondPush()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var approvalTime = created.AddHours(1);
        var secondPush = created.AddHours(2);

        var pr = CreateCompletedPr(
            creationDate: created,
            reviewers:
            [
                new ReviewerInfo
                {
                    DisplayName = "Reviewer One", Id = "reviewer-1",
                    Vote = 10, IsContainer = false, IsRequired = true,
                },
            ],
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = approvalTime,
                    AuthorDisplayName = "Reviewer One", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "create" },
                new IterationInfo { IterationId = 2, CreatedDate = secondPush, Reason = "push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.IsFirstTimeApproval.Should().BeTrue();
    }

    [Fact]
    public void CalculatePerPR_NotFirstTimeApproval_ApprovedAfterSecondPush()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var secondPush = created.AddHours(1);
        var approvalTime = created.AddHours(2);

        var pr = CreateCompletedPr(
            creationDate: created,
            reviewers:
            [
                new ReviewerInfo
                {
                    DisplayName = "Reviewer One", Id = "reviewer-1",
                    Vote = 10, IsContainer = false, IsRequired = true,
                },
            ],
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = approvalTime,
                    AuthorDisplayName = "Reviewer One", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "create" },
                new IterationInfo { IterationId = 2, CreatedDate = secondPush, Reason = "push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.IsFirstTimeApproval.Should().BeFalse();
    }

    [Fact]
    public void CalculatePerPR_FirstTimeApproval_SingleIterationWithApproval()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var pr = CreateCompletedPr(
            creationDate: created,
            reviewers:
            [
                new ReviewerInfo
                {
                    DisplayName = "Reviewer One", Id = "reviewer-1",
                    Vote = 10, IsContainer = false, IsRequired = true,
                },
            ],
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer One", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "create" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.IsFirstTimeApproval.Should().BeTrue();
    }

    [Fact]
    public void CalculatePerPR_AbandonedPr_NoCycleTimeCalculated()
    {
        var pr = new PullRequestData
        {
            PullRequestId = 1,
            Title = "Abandoned PR",
            RepositoryName = "test-repo",
            Status = PrStatus.Abandoned,
            IsDraft = false,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedDate = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            AuthorDisplayName = "Author One",
            AuthorId = "author-1",
            Reviewers = [],
            Threads = [],
            Iterations = [],
            FilesChanged = 0,
            CommitCount = 0,
        };

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().BeNull();
    }

    [Fact]
    public void CalculatePerPR_ActivePr_HasActiveAge()
    {
        var pr = new PullRequestData
        {
            PullRequestId = 1,
            Title = "Active PR",
            RepositoryName = "test-repo",
            Status = PrStatus.Active,
            IsDraft = false,
            CreationDate = DateTime.UtcNow.AddDays(-5),
            ClosedDate = null,
            AuthorDisplayName = "Author One",
            AuthorId = "author-1",
            Reviewers = [],
            Threads = [],
            Iterations = [],
            FilesChanged = 3,
            CommitCount = 1,
        };

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().BeNull();
        metrics.ActiveAge.Should().NotBeNull();
        metrics.ActiveAge!.Value.TotalDays.Should().BeApproximately(5, 0.1);
    }

    [Fact]
    public void CalculatePerPR_DraftPr_ExcludedFromCycleTime()
    {
        var pr = CreateCompletedPr(isDraft: true);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().BeNull();
        metrics.IsDraft.Should().BeTrue();
    }

    [Fact]
    public void CalculatePerPR_AllSystemThreads_ZeroHumanComments()
    {
        var pr = CreateCompletedPr(
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "System", AuthorId = "system-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "system",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "System", AuthorId = "system-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = false,
                },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.HumanCommentCount.Should().Be(0);
    }

    [Fact]
    public void CalculatePerPR_ThreadResolution_CountsCorrectly()
    {
        var pr = CreateCompletedPr(
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "text",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "Reviewer", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "fixed", CommentCount = 2,
                    IsVoteUpdate = false,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "text",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "Reviewer", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = false,
                },
                new ThreadInfo
                {
                    ThreadId = 3, CommentType = "text",
                    PublishedDate = DateTime.UtcNow,
                    AuthorDisplayName = "Bot", AuthorId = "bot-1",
                    IsAuthorBot = true, Status = "fixed", CommentCount = 1,
                    IsVoteUpdate = false,
                },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ResolvableThreadCount.Should().Be(2); // excludes bot thread
        metrics.ResolvedThreadCount.Should().Be(1); // only non-bot fixed thread
    }

    [Fact]
    public void AggregateTeamMetrics_MultiplePrs_CalculatesRatesCorrectly()
    {
        var prs = new List<PullRequestData>
        {
            CreateCompletedPr(id: 1, authorId: "a1", authorName: "Alice",
                reviewers:
                [
                    new ReviewerInfo
                    {
                        DisplayName = "Bob", Id = "b1", Vote = 10,
                        IsContainer = false, IsRequired = true,
                    },
                ]),
            CreateCompletedPr(id: 2, authorId: "b1", authorName: "Bob",
                reviewers:
                [
                    new ReviewerInfo
                    {
                        DisplayName = "Alice", Id = "a1", Vote = 10,
                        IsContainer = false, IsRequired = true,
                    },
                ]),
            CreateCompletedPr(id: 3, authorId: "a1", authorName: "Alice",
                reviewers: []),
        };

        var metrics = prs.Select(_calculator.CalculatePerPR).ToList();
        var team = _calculator.AggregateTeamMetrics(metrics, prs);

        team.TotalPrCount.Should().Be(3);
        team.CompletedPrCount.Should().Be(3);
        team.PrsPerAuthor["Alice"].Should().Be(2);
        team.PrsPerAuthor["Bob"].Should().Be(1);
        team.ReviewsPerPerson.Should().ContainKey("Bob");
        team.ReviewsPerPerson.Should().ContainKey("Alice");
        team.PairingMatrix.Should().ContainKey(new ReviewerAuthorPair("Alice", "Bob"));
    }

    [Fact]
    public void AggregateTeamMetrics_EmptyList_HandlesGracefully()
    {
        var team = _calculator.AggregateTeamMetrics([], []);

        team.TotalPrCount.Should().Be(0);
        team.AbandonedRate.Should().Be(0);
        team.AvgCycleTime.Should().BeNull();
    }

    [Fact]
    public void CalculatePerPR_PropagatesRepositoryName()
    {
        var pr = CreateCompletedPr(repositoryName: "my-special-repo");

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.RepositoryName.Should().Be("my-special-repo");
    }

    [Fact]
    public void AggregateTeamMetrics_MultipleRepos_CalculatesPerRepoBreakdown()
    {
        var prs = new List<PullRequestData>
        {
            CreateCompletedPr(id: 1, repositoryName: "repo-a", authorId: "a1", authorName: "Alice",
                reviewers:
                [
                    new ReviewerInfo { DisplayName = "Bob", Id = "b1", Vote = 10, IsContainer = false, IsRequired = true },
                ]),
            CreateCompletedPr(id: 2, repositoryName: "repo-a", authorId: "b1", authorName: "Bob",
                reviewers: []),
            CreateCompletedPr(id: 3, repositoryName: "repo-b", authorId: "a1", authorName: "Alice",
                reviewers:
                [
                    new ReviewerInfo { DisplayName = "Bob", Id = "b1", Vote = 10, IsContainer = false, IsRequired = true },
                ]),
        };

        // Add an abandoned PR to repo-b
        prs.Add(new PullRequestData
        {
            PullRequestId = 4,
            Title = "Abandoned PR",
            RepositoryName = "repo-b",
            Status = PrStatus.Abandoned,
            IsDraft = false,
            CreationDate = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedDate = new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            AuthorDisplayName = "Alice",
            AuthorId = "a1",
            Reviewers = [],
            Threads = [],
            Iterations = [],
            FilesChanged = 0,
            CommitCount = 0,
        });

        var metrics = prs.Select(_calculator.CalculatePerPR).ToList();
        var team = _calculator.AggregateTeamMetrics(metrics, prs);

        team.PerRepositoryBreakdown.Should().ContainKey("repo-a");
        team.PerRepositoryBreakdown.Should().ContainKey("repo-b");

        var repoA = team.PerRepositoryBreakdown["repo-a"];
        repoA.TotalPrCount.Should().Be(2);
        repoA.CompletedPrCount.Should().Be(2);
        repoA.AbandonedPrCount.Should().Be(0);
        repoA.AbandonedRate.Should().Be(0);

        var repoB = team.PerRepositoryBreakdown["repo-b"];
        repoB.TotalPrCount.Should().Be(2);
        repoB.CompletedPrCount.Should().Be(1);
        repoB.AbandonedPrCount.Should().Be(1);
        repoB.AbandonedRate.Should().Be(0.5);
    }

    [Fact]
    public void AllRepositories_EmptyList_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            Organization = "https://dev.azure.com/test",
            Project = "TestProject",
            Repositories = [],
            Pat = "fake-pat",
        };

        settings.AllRepositories.Should().BeTrue();
    }

    [Fact]
    public void RepositoryDisplayName_FormatsCorrectly()
    {
        var allRepos = new AppSettings
        {
            Organization = "https://dev.azure.com/test",
            Project = "TestProject",
            Repositories = [],
            Pat = "fake-pat",
        };
        allRepos.RepositoryDisplayName.Should().Be("All Repositories");

        var singleRepo = new AppSettings
        {
            Organization = "https://dev.azure.com/test",
            Project = "TestProject",
            Repositories = ["my-repo"],
            Pat = "fake-pat",
        };
        singleRepo.RepositoryDisplayName.Should().Be("my-repo");

        var multiRepo = new AppSettings
        {
            Organization = "https://dev.azure.com/test",
            Project = "TestProject",
            Repositories = ["repo-a", "repo-b"],
            Pat = "fake-pat",
        };
        multiRepo.RepositoryDisplayName.Should().Be("repo-a, repo-b");
    }

    [Fact]
    public async Task DefaultCommand_ParsesExistingFlags()
    {
        var app = new CommandAppTester();
        app.SetDefaultCommand<PrStatsCommand>();
        app.Configure(config =>
        {
            config.AddCommand<PrStatsCommand>("report");
            config.AddCommand<ChatCommand>("chat");
        });

        // Run with existing flags — command will fail (no PAT) but settings should parse
        var result = await app.RunAsync(new[]
        {
            "--org", "https://dev.azure.com/test",
            "--project", "MyProject",
            "--days", "30",
            "--output", "custom.html",
            "--no-open",
            "--no-cache",
            "--json",
        });

        // Settings should have been parsed correctly even though execution failed
        var settings = result.Settings.Should().BeOfType<PrStatsCommand.Settings>().Subject;
        settings.Organization.Should().Be("https://dev.azure.com/test");
        settings.Project.Should().Be("MyProject");
        settings.Days.Should().Be(30);
        settings.Output.Should().Be("custom.html");
        settings.NoOpen.Should().BeTrue();
        settings.NoCache.Should().BeTrue();
        settings.Json.Should().BeTrue();
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("repo1", 1)]
    [InlineData("repo1,repo2", 2)]
    [InlineData("repo1,,repo2", 2)]
    [InlineData("repo1, repo2 , repo3", 3)]
    [InlineData(",,,", 0)]
    [InlineData("  repo1  ", 1)]
    public void CommaParsingEdgeCases(string input, int expectedCount)
    {
        var result = string.IsNullOrWhiteSpace(input)
            ? new List<string>()
            : input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

        result.Should().HaveCount(expectedCount);
    }
}
