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
        DateTime? publishedDate = null,
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
            PublishedDate = publishedDate,
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

        var metrics = prs.Select(pr => _calculator.CalculatePerPR(pr)).ToList();
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
    public void AggregateTeamMetrics_CommentsPerPerson_CountsThreadsOnOthersPrs()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var prs = new List<PullRequestData>
        {
            CreateCompletedPr(id: 1, authorId: "a1", authorName: "Alice",
                threads:
                [
                    // Valid: Bob comments on Alice's PR (should count)
                    new ThreadInfo
                    {
                        ThreadId = 1, CommentType = "text", PublishedDate = created.AddHours(1),
                        AuthorDisplayName = "Bob", AuthorId = "b1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                    // Excluded: Alice comments on her own PR (self-comment)
                    new ThreadInfo
                    {
                        ThreadId = 2, CommentType = "text", PublishedDate = created.AddHours(2),
                        AuthorDisplayName = "Alice", AuthorId = "a1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                    // Excluded: Bot comment
                    new ThreadInfo
                    {
                        ThreadId = 3, CommentType = "text", PublishedDate = created.AddHours(3),
                        AuthorDisplayName = "CI Bot", AuthorId = "bot-1",
                        IsAuthorBot = true, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                    // Excluded: Vote update thread
                    new ThreadInfo
                    {
                        ThreadId = 4, CommentType = "system", PublishedDate = created.AddHours(4),
                        AuthorDisplayName = "Bob", AuthorId = "b1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = true, VoteValue = 10,
                    },
                    // Excluded: system comment type (not "text")
                    new ThreadInfo
                    {
                        ThreadId = 5, CommentType = "system", PublishedDate = created.AddHours(5),
                        AuthorDisplayName = "Charlie", AuthorId = "c1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                    // Valid: Charlie comments on Alice's PR (should count)
                    new ThreadInfo
                    {
                        ThreadId = 6, CommentType = "text", PublishedDate = created.AddHours(6),
                        AuthorDisplayName = "Charlie", AuthorId = "c1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                ]),
            CreateCompletedPr(id: 2, authorId: "b1", authorName: "Bob",
                threads:
                [
                    // Valid: Alice comments on Bob's PR (should count)
                    new ThreadInfo
                    {
                        ThreadId = 7, CommentType = "text", PublishedDate = created.AddHours(1),
                        AuthorDisplayName = "Alice", AuthorId = "a1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = false,
                    },
                ]),
        };

        var metrics = prs.Select(pr => _calculator.CalculatePerPR(pr)).ToList();
        var team = _calculator.AggregateTeamMetrics(metrics, prs);

        team.CommentsPerPerson.Should().HaveCount(3);
        team.CommentsPerPerson["Bob"].Should().Be(1);
        team.CommentsPerPerson["Charlie"].Should().Be(1);
        team.CommentsPerPerson["Alice"].Should().Be(1);
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

        var metrics = prs.Select(pr => _calculator.CalculatePerPR(pr)).ToList();
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

    // --- Approval Reset Count Tests ---

    [Fact]
    public void ApprovalResetCount_NoApprovals_ZeroResets()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads: [],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(2), Reason = "Push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(0);
    }

    [Fact]
    public void ApprovalResetCount_ApprovalWithNoSubsequentPush_ZeroResets()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(0);
    }

    [Fact]
    public void ApprovalResetCount_ApprovalThenPush_OneReset()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(2), Reason = "Push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(1);
    }

    [Fact]
    public void ApprovalResetCount_MultipleCycles_TwoResets()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "system", PublishedDate = created.AddHours(3),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(2), Reason = "Push" },
                new IterationInfo { IterationId = 3, CreatedDate = created.AddHours(4), Reason = "ForcePush" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(2);
    }

    [Fact]
    public void ApprovalResetCount_PushBeforeApproval_ZeroResets()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(3),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(1), Reason = "Push" },
                new IterationInfo { IterationId = 3, CreatedDate = created.AddHours(2), Reason = "Push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(0);
    }

    [Fact]
    public void ApprovalResetCount_MultipleApprovalsThenSinglePush_OneReset()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = CreateCompletedPr(
            creationDate: created,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer A", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "system", PublishedDate = created.AddHours(2),
                    AuthorDisplayName = "Reviewer B", AuthorId = "r2",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 5,
                },
            ],
            iterations:
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(3), Reason = "Push" },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(1);
    }

    [Fact]
    public void ApprovalResetCount_ActivePr_ZeroResets()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr = new PullRequestData
        {
            PullRequestId = 1,
            Title = "Active PR",
            RepositoryName = "test-repo",
            Status = PrStatus.Active,
            IsDraft = false,
            CreationDate = created,
            ClosedDate = null,
            AuthorDisplayName = "Author One",
            AuthorId = "author-1",
            Reviewers = [],
            Threads =
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer", AuthorId = "r1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ],
            Iterations =
            [
                new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(2), Reason = "Push" },
            ],
            FilesChanged = 5,
            CommitCount = 3,
        };

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.ApprovalResetCount.Should().Be(0);
    }

    // --- Draft-Aware Cycle Time Tests ---

    [Fact]
    public void CycleTime_WithPublishedDate_UsesPublishedDate()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var published = created.AddHours(4);
        var closed = created.AddHours(28);

        var pr = CreateCompletedPr(
            creationDate: created,
            publishedDate: published,
            closedDate: closed);

        var metrics = _calculator.CalculatePerPR(pr);

        // Cycle time should be from published to closed (24h), not created to closed (28h)
        metrics.TotalCycleTime.Should().Be(TimeSpan.FromHours(24));
        metrics.PublishedDate.Should().Be(published);
    }

    [Fact]
    public void CycleTime_WithoutPublishedDate_FallsBackToCreationDate()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var closed = created.AddHours(48);

        var pr = CreateCompletedPr(
            creationDate: created,
            publishedDate: null,
            closedDate: closed);

        var metrics = _calculator.CalculatePerPR(pr);

        metrics.TotalCycleTime.Should().Be(TimeSpan.FromHours(48));
        metrics.PublishedDate.Should().BeNull();
    }

    [Fact]
    public void CycleTime_PublishedDate_AffectsTimeToFirstCommentAndApproval()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var published = created.AddHours(4);
        var commentTime = created.AddHours(6); // 2h after published
        var approvalTime = created.AddHours(8); // 4h after published
        var closed = created.AddHours(28);

        var pr = CreateCompletedPr(
            creationDate: created,
            publishedDate: published,
            closedDate: closed,
            threads:
            [
                new ThreadInfo
                {
                    ThreadId = 1, CommentType = "text", PublishedDate = commentTime,
                    AuthorDisplayName = "Reviewer", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = false,
                },
                new ThreadInfo
                {
                    ThreadId = 2, CommentType = "system", PublishedDate = approvalTime,
                    AuthorDisplayName = "Reviewer", AuthorId = "reviewer-1",
                    IsAuthorBot = false, Status = "active", CommentCount = 1,
                    IsVoteUpdate = true, VoteValue = 10,
                },
            ]);

        var metrics = _calculator.CalculatePerPR(pr);

        // Should be from published (not created), so 2h and 4h respectively
        metrics.TimeToFirstHumanComment.Should().Be(TimeSpan.FromHours(2));
        metrics.TimeToFirstApproval.Should().Be(TimeSpan.FromHours(4));
    }

    // --- Team Metrics: Approval Reset Rate ---

    [Fact]
    public void AggregateTeamMetrics_ApprovalResetRate_CalculatedCorrectly()
    {
        var created = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var prs = new List<PullRequestData>
        {
            // PR 1: has approval reset
            CreateCompletedPr(id: 1, creationDate: created,
                threads:
                [
                    new ThreadInfo
                    {
                        ThreadId = 1, CommentType = "system", PublishedDate = created.AddHours(1),
                        AuthorDisplayName = "Reviewer", AuthorId = "r1",
                        IsAuthorBot = false, Status = "active", CommentCount = 1,
                        IsVoteUpdate = true, VoteValue = 10,
                    },
                ],
                iterations:
                [
                    new IterationInfo { IterationId = 1, CreatedDate = created, Reason = "Create" },
                    new IterationInfo { IterationId = 2, CreatedDate = created.AddHours(2), Reason = "Push" },
                ]),
            // PR 2: no resets
            CreateCompletedPr(id: 2, creationDate: created),
            // PR 3: no resets
            CreateCompletedPr(id: 3, creationDate: created),
        };

        var metrics = prs.Select(pr => _calculator.CalculatePerPR(pr)).ToList();
        var team = _calculator.AggregateTeamMetrics(metrics, prs);

        // 1 out of 3 completed PRs had resets
        team.ApprovalResetRate.Should().BeApproximately(1.0 / 3.0, 0.001);
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

    // --- Build Metrics Tests ---

    private static BuildInfo CreateBuild(
        int id = 1,
        string definitionName = "CI Pipeline",
        int definitionId = 100,
        string status = "Completed",
        string? result = "Succeeded",
        DateTime? queueTime = null,
        DateTime? startTime = null,
        DateTime? finishTime = null)
    {
        var queue = queueTime ?? new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        return new BuildInfo
        {
            BuildId = id,
            DefinitionName = definitionName,
            DefinitionId = definitionId,
            Status = status,
            Result = result,
            QueueTime = queue,
            StartTime = startTime ?? queue.AddMinutes(2),
            FinishTime = finishTime ?? queue.AddMinutes(12),
        };
    }

    [Fact]
    public void BuildMetrics_MixedResults_CorrectCountsAndSuccessRate()
    {
        var builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, result: "Succeeded"),
            CreateBuild(id: 2, result: "Failed"),
            CreateBuild(id: 3, result: "Succeeded"),
            CreateBuild(id: 4, result: "Canceled"),
            CreateBuild(id: 5, result: "PartiallySucceeded"),
        };

        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, builds);

        metrics.BuildMetrics.Should().NotBeNull();
        metrics.BuildMetrics!.TotalBuildCount.Should().Be(5);
        metrics.BuildMetrics.SucceededCount.Should().Be(2);
        metrics.BuildMetrics.FailedCount.Should().Be(1);
        metrics.BuildMetrics.CanceledCount.Should().Be(1);
        metrics.BuildMetrics.PartiallySucceededCount.Should().Be(1);
        // Success rate: 2 / (2 + 1 + 1) = 0.5 (canceled excluded)
        metrics.BuildMetrics.BuildSuccessRate.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void BuildMetrics_EmptyBuilds_NullBuildMetrics()
    {
        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, []);

        metrics.BuildMetrics.Should().BeNull();
    }

    [Fact]
    public void BuildMetrics_NullBuilds_NullBuildMetrics()
    {
        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, null);

        metrics.BuildMetrics.Should().BeNull();
    }

    [Fact]
    public void BuildMetrics_CanceledExcludedFromSuccessRate()
    {
        var builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, result: "Succeeded"),
            CreateBuild(id: 2, result: "Canceled"),
            CreateBuild(id: 3, result: "Canceled"),
        };

        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, builds);

        // Only 1 terminal outcome (succeeded), so rate = 1/1 = 100%
        metrics.BuildMetrics!.BuildSuccessRate.Should().Be(1.0);
    }

    [Fact]
    public void BuildMetrics_TimingsCalculatedCorrectly()
    {
        var queue = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, queueTime: queue, startTime: queue.AddMinutes(2), finishTime: queue.AddMinutes(12)),
            CreateBuild(id: 2, queueTime: queue.AddMinutes(20), startTime: queue.AddMinutes(24), finishTime: queue.AddMinutes(34)),
        };

        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, builds);

        // Avg queue time: (2min + 4min) / 2 = 3min
        metrics.BuildMetrics!.AvgQueueTime.Should().Be(TimeSpan.FromMinutes(3));
        // Avg run time: (10min + 10min) / 2 = 10min
        metrics.BuildMetrics.AvgRunTime.Should().Be(TimeSpan.FromMinutes(10));
        // Total elapsed: 12min + 14min = 26min
        metrics.BuildMetrics.TotalElapsedTime.Should().Be(TimeSpan.FromMinutes(26));
        // Total run time: 10min + 10min = 20min
        metrics.BuildMetrics.TotalRunTime.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void BuildMetrics_NullStartAndFinishTime_ExcludedFromDurations()
    {
        var queue = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, queueTime: queue, startTime: queue.AddMinutes(2), finishTime: queue.AddMinutes(12)),
            new BuildInfo
            {
                BuildId = 2, DefinitionName = "CI Pipeline", DefinitionId = 100,
                Status = "InProgress", Result = null,
                QueueTime = queue.AddMinutes(20), StartTime = null, FinishTime = null,
            },
        };

        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, builds);

        metrics.BuildMetrics!.TotalBuildCount.Should().Be(2);
        // Only first build has timing data
        metrics.BuildMetrics.AvgQueueTime.Should().Be(TimeSpan.FromMinutes(2));
        metrics.BuildMetrics.AvgRunTime.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void BuildMetrics_MultiplePipelines_GroupedCorrectly()
    {
        var queue = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, definitionName: "Build", result: "Succeeded"),
            CreateBuild(id: 2, definitionName: "Build", result: "Succeeded"),
            CreateBuild(id: 3, definitionName: "Deploy", result: "Failed"),
            CreateBuild(id: 4, definitionName: "Deploy", result: "Succeeded"),
        };

        var pr = CreateCompletedPr();
        var metrics = _calculator.CalculatePerPR(pr, builds);

        metrics.BuildMetrics!.PerPipeline.Should().HaveCount(2);
        var buildPipeline = metrics.BuildMetrics.PerPipeline.Single(p => p.DefinitionName == "Build");
        buildPipeline.RunCount.Should().Be(2);
        buildPipeline.SucceededCount.Should().Be(2);
        buildPipeline.FailedCount.Should().Be(0);

        var deployPipeline = metrics.BuildMetrics.PerPipeline.Single(p => p.DefinitionName == "Deploy");
        deployPipeline.RunCount.Should().Be(2);
        deployPipeline.SucceededCount.Should().Be(1);
        deployPipeline.FailedCount.Should().Be(1);
    }

    [Fact]
    public void AggregateTeamMetrics_WithBuilds_AggregatesCorrectly()
    {
        var queue = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var pr1Builds = new List<BuildInfo>
        {
            CreateBuild(id: 1, result: "Succeeded"),
            CreateBuild(id: 2, result: "Failed"),
        };
        var pr2Builds = new List<BuildInfo>
        {
            CreateBuild(id: 3, result: "Succeeded"),
            CreateBuild(id: 4, result: "Succeeded"),
            CreateBuild(id: 5, result: "Succeeded"),
        };

        var prs = new List<PullRequestData>
        {
            CreateCompletedPr(id: 1),
            CreateCompletedPr(id: 2),
            CreateCompletedPr(id: 3), // no builds
        };

        var prMetrics = new List<PullRequestMetrics>
        {
            _calculator.CalculatePerPR(prs[0], pr1Builds),
            _calculator.CalculatePerPR(prs[1], pr2Builds),
            _calculator.CalculatePerPR(prs[2], null),
        };

        var team = _calculator.AggregateTeamMetrics(prMetrics, prs);

        team.BuildMetrics.Should().NotBeNull();
        team.BuildMetrics!.TotalBuildsAcrossAllPrs.Should().Be(5);
        team.BuildMetrics.AvgBuildsPerPr.Should().Be(2.5); // (2+3)/2
        team.BuildMetrics.MedianBuildsPerPr.Should().Be(2.5); // median of [2,3]
        // 4 succeeded out of 4+1 terminal = 80%
        team.BuildMetrics.OverallBuildSuccessRate.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void AggregateTeamMetrics_NoBuilds_NullTeamBuildMetrics()
    {
        var prs = new List<PullRequestData>
        {
            CreateCompletedPr(id: 1),
            CreateCompletedPr(id: 2),
        };

        var prMetrics = prs.Select(pr => _calculator.CalculatePerPR(pr)).ToList();
        var team = _calculator.AggregateTeamMetrics(prMetrics, prs);

        team.BuildMetrics.Should().BeNull();
    }
}
