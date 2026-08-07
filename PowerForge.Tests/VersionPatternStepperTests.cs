namespace PowerForge.Tests;

public sealed class VersionPatternStepperTests
{
    [Theory]
    [InlineData("1.X", "1.5.0", "1.6")]
    [InlineData("X.0.0", "1.5.0", "2.0.0")]
    [InlineData("1.5.X", "1.5.0", "1.5.1")]
    [InlineData("1.5.0.X", "1.5.0", "1.5.0.0")]
    public void Step_UsesTheSharedPSPublishModulePatternContract(
        string pattern,
        string current,
        string expected)
    {
        Assert.Equal(expected, VersionPatternStepper.Step(pattern, Version.Parse(current)));
    }

    [Fact]
    public void Step_RejectsMoreThanOnePlaceholder()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VersionPatternStepper.Step("X.X.X", Version.Parse("1.5.0")));

        Assert.Contains("only one 'X' placeholder", exception.Message, StringComparison.Ordinal);
    }
}
