using FluentAssertions;
using PrStats.Configuration;

namespace PrStats.Tests.Configuration;

public class CompareCommandTests
{
    [Fact]
    public void Validate_FewerThanTwoFiles_ReturnsError()
    {
        var settings = new CompareCommand.Settings
        {
            Files = ["file1.json"],
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("At least 2");
    }

    [Fact]
    public void Validate_NullFiles_ReturnsError()
    {
        var settings = new CompareCommand.Settings
        {
            Files = null,
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("At least 2");
    }

    [Fact]
    public void Validate_MoreThanFiveFiles_ReturnsError()
    {
        var settings = new CompareCommand.Settings
        {
            Files = ["a.json", "b.json", "c.json", "d.json", "e.json", "f.json"],
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("At most 5");
    }

    [Fact]
    public void Validate_MismatchedLabelCount_ReturnsError()
    {
        var settings = new CompareCommand.Settings
        {
            Files = ["a.json", "b.json"],
            Labels = "Team A,Team B,Team C",
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("count");
    }

    [Fact]
    public void Validate_ValidInput_Succeeds()
    {
        var settings = new CompareCommand.Settings
        {
            Files = ["a.json", "b.json"],
            Labels = "Team A,Team B",
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidInputWithoutLabels_Succeeds()
    {
        var settings = new CompareCommand.Settings
        {
            Files = ["a.json", "b.json", "c.json"],
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }
}
