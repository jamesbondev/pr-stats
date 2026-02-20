using FluentAssertions;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class AzureDevOpsClientTests
{
    private static GitPullRequestCommentThread CreateSystemThread(
        string content, DateTime publishedDate)
    {
        return new GitPullRequestCommentThread
        {
            Id = 1,
            PublishedDate = publishedDate,
            Comments =
            [
                new Comment
                {
                    CommentType = CommentType.System,
                    Content = content,
                },
            ],
        };
    }

    private static GitPullRequestCommentThread CreateTextThread(
        string content, DateTime publishedDate)
    {
        return new GitPullRequestCommentThread
        {
            Id = 2,
            PublishedDate = publishedDate,
            Comments =
            [
                new Comment
                {
                    CommentType = CommentType.Text,
                    Content = content,
                },
            ],
        };
    }

    [Fact]
    public void DetectPublishedDate_SystemThreadContainsPublished_ReturnsTimestamp()
    {
        var timestamp = new DateTime(2025, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        var threads = new List<GitPullRequestCommentThread>
        {
            CreateSystemThread("Author published this pull request", timestamp),
        };

        var result = AzureDevOpsClient.DetectPublishedDate(threads);

        result.Should().Be(timestamp);
    }

    [Fact]
    public void DetectPublishedDate_NoSystemThreads_ReturnsNull()
    {
        var threads = new List<GitPullRequestCommentThread>
        {
            CreateTextThread("Some regular comment", DateTime.UtcNow),
        };

        var result = AzureDevOpsClient.DetectPublishedDate(threads);

        result.Should().BeNull();
    }

    [Fact]
    public void DetectPublishedDate_SystemThreadWithUnrelatedContent_ReturnsNull()
    {
        var threads = new List<GitPullRequestCommentThread>
        {
            CreateSystemThread("Author completed the pull request", DateTime.UtcNow),
            CreateSystemThread("Author updated reviewers", DateTime.UtcNow),
            CreateSystemThread("Merge status changed to succeeded", DateTime.UtcNow),
        };

        var result = AzureDevOpsClient.DetectPublishedDate(threads);

        result.Should().BeNull();
    }

    [Fact]
    public void DetectPublishedDate_MultipleSystemThreads_ReturnsCorrectTimestamp()
    {
        var earlyTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var publishTime = new DateTime(2025, 1, 5, 14, 30, 0, DateTimeKind.Utc);
        var lateTime = new DateTime(2025, 1, 10, 10, 0, 0, DateTimeKind.Utc);

        var threads = new List<GitPullRequestCommentThread>
        {
            CreateSystemThread("Author updated reviewers", earlyTime),
            CreateSystemThread("Author published this pull request", publishTime),
            CreateSystemThread("Author completed the pull request", lateTime),
        };

        var result = AzureDevOpsClient.DetectPublishedDate(threads);

        result.Should().Be(publishTime);
    }
}
