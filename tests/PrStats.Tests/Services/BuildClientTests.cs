using FluentAssertions;
using Microsoft.TeamFoundation.Build.WebApi;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class BuildClientTests
{
    [Fact]
    public void MapBuild_FullyPopulated_MapsAllFields()
    {
        var build = new Build
        {
            Id = 42,
            Definition = new DefinitionReference { Id = 10, Name = "CI Pipeline" },
            Status = BuildStatus.Completed,
            Result = BuildResult.Succeeded,
            QueueTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            StartTime = new DateTime(2025, 1, 1, 10, 2, 0, DateTimeKind.Utc),
            FinishTime = new DateTime(2025, 1, 1, 10, 12, 0, DateTimeKind.Utc),
            SourceVersion = "abc123",
        };

        var result = BuildClient.MapBuild(build);

        result.BuildId.Should().Be(42);
        result.DefinitionName.Should().Be("CI Pipeline");
        result.DefinitionId.Should().Be(10);
        result.Status.Should().Be("Completed");
        result.Result.Should().Be("Succeeded");
        result.QueueTime.Should().Be(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        result.StartTime.Should().Be(new DateTime(2025, 1, 1, 10, 2, 0, DateTimeKind.Utc));
        result.FinishTime.Should().Be(new DateTime(2025, 1, 1, 10, 12, 0, DateTimeKind.Utc));
        result.SourceVersion.Should().Be("abc123");
    }

    [Fact]
    public void MapBuild_NullDefinition_DefaultsToUnknown()
    {
        var build = new Build
        {
            Id = 1,
            Definition = null,
            Status = BuildStatus.Completed,
            Result = BuildResult.Failed,
            QueueTime = DateTime.UtcNow,
        };

        var result = BuildClient.MapBuild(build);

        result.DefinitionName.Should().Be("Unknown");
        result.DefinitionId.Should().Be(0);
    }

    [Fact]
    public void MapBuild_NullTimes_MapsAsNull()
    {
        var build = new Build
        {
            Id = 1,
            Definition = new DefinitionReference { Id = 5, Name = "Test" },
            Status = BuildStatus.NotStarted,
            Result = null,
            QueueTime = null,
            StartTime = null,
            FinishTime = null,
        };

        var result = BuildClient.MapBuild(build);

        result.StartTime.Should().BeNull();
        result.FinishTime.Should().BeNull();
        result.Result.Should().BeNull();
        result.QueueTime.Should().Be(DateTime.MinValue);
    }

    [Theory]
    [InlineData(BuildStatus.Completed, "Completed")]
    [InlineData(BuildStatus.InProgress, "InProgress")]
    [InlineData(BuildStatus.NotStarted, "NotStarted")]
    [InlineData(BuildStatus.Cancelling, "Cancelling")]
    [InlineData(BuildStatus.Postponed, "Postponed")]
    public void MapBuild_StatusMappings(BuildStatus status, string expected)
    {
        var build = new Build
        {
            Id = 1,
            Definition = new DefinitionReference { Id = 1, Name = "Test" },
            Status = status,
            QueueTime = DateTime.UtcNow,
        };

        var result = BuildClient.MapBuild(build);

        result.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(BuildResult.Succeeded, "Succeeded")]
    [InlineData(BuildResult.Failed, "Failed")]
    [InlineData(BuildResult.Canceled, "Canceled")]
    [InlineData(BuildResult.PartiallySucceeded, "PartiallySucceeded")]
    public void MapBuild_ResultMappings(BuildResult buildResult, string expected)
    {
        var build = new Build
        {
            Id = 1,
            Definition = new DefinitionReference { Id = 1, Name = "Test" },
            Status = BuildStatus.Completed,
            Result = buildResult,
            QueueTime = DateTime.UtcNow,
        };

        var result = BuildClient.MapBuild(build);

        result.Result.Should().Be(expected);
    }

    [Fact]
    public void MapBuild_NullStatus_DefaultsToUnknown()
    {
        var build = new Build
        {
            Id = 1,
            Definition = new DefinitionReference { Id = 1, Name = "Test" },
            Status = null,
            QueueTime = DateTime.UtcNow,
        };

        var result = BuildClient.MapBuild(build);

        result.Status.Should().Be("Unknown");
    }
}
