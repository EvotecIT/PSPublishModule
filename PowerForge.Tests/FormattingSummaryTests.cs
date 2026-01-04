using Xunit;

namespace PowerForge.Tests;

public sealed class FormattingSummaryTests
{
    [Fact]
    public void IsErrorMessage_StillMatchesNoResultReturnedWithDetailsSuffix()
    {
        Assert.True(FormattingSummary.IsErrorMessage("No result returned; pre=0; pssa=0; norm=0"));
    }

    [Fact]
    public void IsSkippedMessage_StillMatchesSkippedWithDetailsSuffix()
    {
        Assert.True(FormattingSummary.IsSkippedMessage("Skipped: Timeout; pre=0; pssa=0; norm=0"));
        Assert.False(FormattingSummary.IsErrorMessage("Skipped: Timeout; pre=0; pssa=0; norm=0"));
    }

    [Fact]
    public void IsSkippedMessage_DoesNotGetClassifiedAsErrorEvenIfContainsFailedWord()
    {
        var msg = "Skipped: PSSA failed (exit 1); pre=0; pssa=0; norm=0";
        Assert.True(FormattingSummary.IsSkippedMessage(msg));
        Assert.False(FormattingSummary.IsErrorMessage(msg));
    }
}

