using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebPipelineRunnerFailureFormattingTests
{
    [Fact]
    public void FormatFailureMessage_UsesInnerExceptionMessage_WhenOuterMessageIsBlank()
    {
        var exception = new InvalidOperationException(string.Empty, new IOException("The file is in use."));

        var message = WebPipelineRunner.FormatFailureMessage(exception);

        Assert.Equal("InvalidOperationException -> IOException: The file is in use.", message);
    }

    [Fact]
    public void FormatFailureMessage_FallsBackToExceptionType_WhenNoMessagesExist()
    {
        var exception = new InvalidOperationException(string.Empty, new Exception(string.Empty));

        var message = WebPipelineRunner.FormatFailureMessage(exception);

        Assert.Equal("InvalidOperationException", message);
    }
}
