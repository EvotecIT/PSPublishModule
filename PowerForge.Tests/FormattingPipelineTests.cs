using System.Text;

namespace PowerForge.Tests;

public sealed class FormattingPipelineTests
{
    [Fact]
    public void Run_WithoutPreprocessingStillNormalizesAndReportsFormatterFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "module.psd1");
        File.WriteAllText(path, "@{\nRootModule='module.psm1'\n}\n", new UTF8Encoding(false));
        try
        {
            var runner = new RecordingRunner(path);
            var pipeline = new FormattingPipeline(new NullLogger(), runner);

            var result = Assert.Single(pipeline.Run(new[] { path }, new FormatOptions()));

            Assert.Single(runner.Requests);
            Assert.True(result.Changed);
            Assert.StartsWith("Error: formatter rejected input", result.Message);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path).Take(3));
            Assert.Contains("\r\n", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Run_EnabledPreprocessingRetainsItsErrorAndRunsFormatting(int option)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "module.ps1");
        File.WriteAllText(path, "param()\n# comment\n\n\n'hello'\n");
        try
        {
            var runner = new RecordingRunner(path);
            var pipeline = new FormattingPipeline(new NullLogger(), runner);
            var options = new FormatOptions
            {
                RemoveCommentsInParamBlock = option == 0,
                RemoveCommentsBeforeParamBlock = option == 1,
                RemoveAllEmptyLines = option == 2,
                RemoveEmptyLines = option == 3
            };

            var result = Assert.Single(pipeline.Run(new[] { path }, options));

            Assert.Equal(2, runner.Requests.Count);
            Assert.StartsWith("Error: preprocessing rejected input", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingRunner(string path) : IPowerShellRunner
    {
        internal List<PowerShellRunRequest> Requests { get; } = new();

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Requests.Add(request);
            var script = File.ReadAllText(request.ScriptPath!);
            var output = script.Contains("PRE::", StringComparison.Ordinal)
                ? $"PRE::ERROR::{path}::preprocessing rejected input"
                : $"ERROR::{path}::formatter rejected input";
            return new PowerShellRunResult(0, output, string.Empty, "stub");
        }
    }
}
