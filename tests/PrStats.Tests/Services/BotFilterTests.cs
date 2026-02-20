using FluentAssertions;
using PrStats.Services;

namespace PrStats.Tests.Services;

public class BotFilterTests
{
    [Fact]
    public void IsBot_KnownBotName_ReturnsTrue()
    {
        var filter = new BotFilter(["Azure Pipelines", "Dependabot"]);

        filter.IsBot("Azure Pipelines", isContainer: false).Should().BeTrue();
        filter.IsBot("Dependabot", isContainer: false).Should().BeTrue();
    }

    [Fact]
    public void IsBot_CaseInsensitiveMatch_ReturnsTrue()
    {
        var filter = new BotFilter(["Azure Pipelines"]);

        filter.IsBot("azure pipelines", isContainer: false).Should().BeTrue();
        filter.IsBot("AZURE PIPELINES", isContainer: false).Should().BeTrue();
        filter.IsBot("Azure pipelines", isContainer: false).Should().BeTrue();
    }

    [Fact]
    public void IsBot_ContainerIdentity_ReturnsTrue()
    {
        var filter = new BotFilter([]);

        filter.IsBot("Some Team Group", isContainer: true).Should().BeTrue();
    }

    [Fact]
    public void IsBot_UnknownHumanName_ReturnsFalse()
    {
        var filter = new BotFilter(["Azure Pipelines"]);

        filter.IsBot("John Doe", isContainer: false).Should().BeFalse();
    }

    [Fact]
    public void IsBot_EmptyBotList_OnlyMatchesContainers()
    {
        var filter = new BotFilter([]);

        filter.IsBot("Azure Pipelines", isContainer: false).Should().BeFalse();
        filter.IsBot("Some Group", isContainer: true).Should().BeTrue();
    }

    [Fact]
    public void IsBot_EmptyDisplayName_ReturnsFalse()
    {
        var filter = new BotFilter(["Azure Pipelines"]);

        filter.IsBot("", isContainer: false).Should().BeFalse();
    }

    [Fact]
    public void IsBot_KnownBotId_ReturnsTrue()
    {
        var filter = new BotFilter([], ["abc-123", "def-456"]);

        filter.IsBot("Some Bot", isContainer: false, userId: "abc-123").Should().BeTrue();
        filter.IsBot("Another Bot", isContainer: false, userId: "def-456").Should().BeTrue();
    }

    [Fact]
    public void IsBot_BotIdCaseInsensitive_ReturnsTrue()
    {
        var filter = new BotFilter([], ["ABC-123"]);

        filter.IsBot("Some Bot", isContainer: false, userId: "abc-123").Should().BeTrue();
    }

    [Fact]
    public void IsBot_UnknownId_ReturnsFalse()
    {
        var filter = new BotFilter([], ["abc-123"]);

        filter.IsBot("John Doe", isContainer: false, userId: "xyz-999").Should().BeFalse();
    }

    [Fact]
    public void IsBot_MatchByNameOrId_ReturnsTrue()
    {
        var filter = new BotFilter(["Azure Pipelines"], ["bot-id-1"]);

        // Matches by name
        filter.IsBot("Azure Pipelines", isContainer: false, userId: "some-other-id").Should().BeTrue();
        // Matches by ID
        filter.IsBot("Unknown Bot Name", isContainer: false, userId: "bot-id-1").Should().BeTrue();
        // Matches neither
        filter.IsBot("John Doe", isContainer: false, userId: "human-id").Should().BeFalse();
    }

    [Fact]
    public void IsBot_NullUserId_DoesNotMatch()
    {
        var filter = new BotFilter([], ["abc-123"]);

        filter.IsBot("Some Name", isContainer: false, userId: null).Should().BeFalse();
    }
}
