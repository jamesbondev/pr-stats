using FluentAssertions;
using PrStats.Models;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class PrCacheTests : IDisposable
{
    private readonly string _testOrg = "https://dev.azure.com/testorg";
    private readonly string _testProject = "TestProject";
    private readonly string _cachePath;

    public PrCacheTests()
    {
        _cachePath = PrCache.GetCachePath(_testOrg, _testProject);
    }

    public void Dispose()
    {
        if (File.Exists(_cachePath))
            File.Delete(_cachePath);

        var tmpPath = _cachePath + ".tmp";
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);
    }

    private static PullRequestData CreatePr(
        int id = 1,
        PrStatus status = PrStatus.Completed,
        DateTime? creationDate = null,
        DateTime? closedDate = null)
    {
        var created = creationDate ?? DateTime.UtcNow.AddDays(-30);
        var closed = status == PrStatus.Active ? (DateTime?)null : closedDate ?? created.AddHours(24);

        return new PullRequestData
        {
            PullRequestId = id,
            Title = $"PR #{id}",
            RepositoryName = "test-repo",
            Status = status,
            IsDraft = false,
            CreationDate = created,
            ClosedDate = closed,
            AuthorDisplayName = "Test Author",
            AuthorId = "author-1",
            IsAuthorBot = false,
            ClosedByDisplayName = "Closer",
            ClosedById = "closer-1",
            Reviewers =
            [
                new ReviewerInfo
                {
                    DisplayName = "Reviewer One",
                    Id = "rev-1",
                    Vote = 10,
                    IsContainer = false,
                    IsRequired = true,
                },
            ],
            Threads =
            [
                new ThreadInfo
                {
                    ThreadId = 100,
                    CommentType = "text",
                    PublishedDate = created.AddHours(1),
                    AuthorDisplayName = "Reviewer One",
                    AuthorId = "rev-1",
                    IsAuthorBot = false,
                    Status = "active",
                    CommentCount = 2,
                    IsVoteUpdate = false,
                    VoteValue = null,
                },
            ],
            Iterations =
            [
                new IterationInfo
                {
                    IterationId = 1,
                    CreatedDate = created,
                    Reason = "create",
                },
            ],
            FilesChanged = 5,
            CommitCount = 3,
        };
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips_AllPrFields()
    {
        var pr = CreatePr(id: 42);
        var prs = new Dictionary<int, PullRequestData> { [42] = pr };

        await PrCache.SaveAsync(_testOrg, _testProject, prs);
        var loaded = await PrCache.LoadAsync(_testOrg, _testProject);

        loaded.Should().ContainKey(42);
        var roundTripped = loaded[42];

        roundTripped.PullRequestId.Should().Be(42);
        roundTripped.Title.Should().Be("PR #42");
        roundTripped.RepositoryName.Should().Be("test-repo");
        roundTripped.Status.Should().Be(PrStatus.Completed);
        roundTripped.IsDraft.Should().BeFalse();
        roundTripped.CreationDate.Should().Be(pr.CreationDate);
        roundTripped.ClosedDate.Should().Be(pr.ClosedDate);
        roundTripped.AuthorDisplayName.Should().Be("Test Author");
        roundTripped.AuthorId.Should().Be("author-1");
        roundTripped.IsAuthorBot.Should().BeFalse();
        roundTripped.ClosedByDisplayName.Should().Be("Closer");
        roundTripped.ClosedById.Should().Be("closer-1");
        roundTripped.FilesChanged.Should().Be(5);
        roundTripped.CommitCount.Should().Be(3);

        roundTripped.Reviewers.Should().HaveCount(1);
        roundTripped.Reviewers[0].DisplayName.Should().Be("Reviewer One");
        roundTripped.Reviewers[0].Vote.Should().Be(10);
        roundTripped.Reviewers[0].IsRequired.Should().BeTrue();

        roundTripped.Threads.Should().HaveCount(1);
        roundTripped.Threads[0].ThreadId.Should().Be(100);
        roundTripped.Threads[0].CommentType.Should().Be("text");
        roundTripped.Threads[0].CommentCount.Should().Be(2);

        roundTripped.Iterations.Should().HaveCount(1);
        roundTripped.Iterations[0].IterationId.Should().Be(1);
        roundTripped.Iterations[0].Reason.Should().Be("create");
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        var result = await PrCache.LoadAsync("https://dev.azure.com/nonexistent", "NoSuchProject");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsEmpty()
    {
        var dir = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_cachePath, "{ this is not valid json !!!");

        var result = await PrCache.LoadAsync(_testOrg, _testProject);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_SchemaMismatch_ReturnsEmpty()
    {
        var dir = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_cachePath, """
            {
                "schemaVersion": 999,
                "organization": "https://dev.azure.com/testorg",
                "project": "TestProject",
                "pullRequests": {}
            }
            """);

        var result = await PrCache.LoadAsync(_testOrg, _testProject);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_EvictsOldPrs()
    {
        var recentPr = CreatePr(id: 1, creationDate: DateTime.UtcNow.AddDays(-10));
        var oldPr = CreatePr(id: 2, creationDate: DateTime.UtcNow.AddDays(-200), closedDate: DateTime.UtcNow.AddDays(-190));

        var prs = new Dictionary<int, PullRequestData>
        {
            [1] = recentPr,
            [2] = oldPr,
        };

        await PrCache.SaveAsync(_testOrg, _testProject, prs);
        var loaded = await PrCache.LoadAsync(_testOrg, _testProject);

        loaded.Should().ContainKey(1);
        loaded.Should().NotContainKey(2);
    }

    [Fact]
    public async Task Save_AtomicWrite_TempFileRenamed()
    {
        var pr = CreatePr(id: 1);
        var prs = new Dictionary<int, PullRequestData> { [1] = pr };

        await PrCache.SaveAsync(_testOrg, _testProject, prs);

        File.Exists(_cachePath).Should().BeTrue();
        File.Exists(_cachePath + ".tmp").Should().BeFalse();

        // Verify it's valid JSON by loading it
        var loaded = await PrCache.LoadAsync(_testOrg, _testProject);
        loaded.Should().ContainKey(1);
    }

    [Fact]
    public void GetCachePath_CaseInsensitive()
    {
        var path1 = PrCache.GetCachePath("https://dev.azure.com/MyOrg", "MyProject");
        var path2 = PrCache.GetCachePath("https://dev.azure.com/myorg", "myproject");

        // Hash portion should be the same since we lowercase before hashing
        var dir1 = Path.GetDirectoryName(path1)!;
        var dir2 = Path.GetDirectoryName(path2)!;
        var hash1 = Path.GetFileName(path1).Split('-')[0];
        var hash2 = Path.GetFileName(path2).Split('-')[0];

        hash1.Should().Be(hash2);
    }
}
