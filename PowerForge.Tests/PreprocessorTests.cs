namespace PowerForge.Tests;

public sealed class PreprocessorTests
{
    [Fact]
    public void Process_PreservesCompletedErrorsAndChangesWhenLaterFilesTimeOut()
    {
        var preprocessor = new Preprocessor(new TimeoutRunner(), new NullLogger());

        var results = preprocessor.Process(
            new[] { "first.ps1", "second.ps1", "third.ps1" },
            new FormatOptions { RemoveEmptyLines = true });

        Assert.Collection(results,
            result =>
            {
                Assert.Equal("first.ps1", result.Path);
                Assert.Equal("Error: Access denied", result.Message);
                Assert.False(result.Changed);
            },
            result =>
            {
                Assert.Equal("second.ps1", result.Path);
                Assert.Equal("Preprocessed", result.Message);
                Assert.True(result.Changed);
            },
            result =>
            {
                Assert.Equal("third.ps1", result.Path);
                Assert.Equal("Skipped: Timeout", result.Message);
                Assert.False(result.Changed);
            });
        var summary = FormattingSummary.FromResults(results);
        Assert.Equal(CheckStatus.Fail, summary.Status);
        Assert.Equal(1, summary.Errors);
    }

    private sealed class TimeoutRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => new(124, "PRE::ERROR::first.ps1::Access denied\r\nPRE::CHANGED::second.ps1\r\n", string.Empty, "stub");
    }
}
