using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationSchemaCaseTests
{
    [Theory]
    [InlineData("windows", true)]
    [InlineData("WiNdOwS", true)]
    [InlineData("linux", true)]
    [InlineData("LiNuX", true)]
    [InlineData("osx", true)]
    [InlineData("OsX", true)]
    [InlineData("Linuz", false)]
    [InlineData("", false)]
    [InlineData("WindowsExtra", false)]
    [InlineData("Linux\n", false)]
    public async Task Platform_schema_and_runtime_agree_on_casing_and_unknown_names(string platform, bool expectedValid)
    {
        var command = new ReleaseCommandValidation { Name = "Casing probe", FileName = "probe", Platforms = [platform] };
        AssertSchemaValidity(command, expectedValid);
        var runner = new Runner("observed");

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Commands = [command] },
            request: new() { ProjectRoot = Path.GetTempPath() });

        Assert.Equal(expectedValid, report.Success);
        var currentPlatform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "OSX" : "Linux";
        var shouldExecute = expectedValid && string.Equals(platform, currentPlatform, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(shouldExecute ? 1 : 0, runner.Calls);
        if (!expectedValid) { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
        else if (shouldExecute) Assert.Equal("Casing probe", Assert.Single(report.Checks));
        else Assert.Contains("not applicable", Assert.Single(report.Checks));
    }

    [Theory]
    [InlineData("array", "[]", true)]
    [InlineData("oBjEcT", "{}", true)]
    [InlineData("STRING", "\"fixture\"", true)]
    [InlineData("nUmBeR", "42", true)]
    [InlineData("true", "true", true)]
    [InlineData("FaLsE", "false", true)]
    [InlineData("NULL", "null", true)]
    [InlineData("Undefined", "null", false)]
    [InlineData("Objects", "{}", false)]
    public async Task Json_kind_schema_and_runtime_agree_on_casing_and_unknown_names(string kind, string output, bool expectedValid)
    {
        var command = new ReleaseCommandValidation { Name = "JSON casing probe", FileName = "probe", OutputJsonKind = kind };
        AssertSchemaValidity(command, expectedValid);
        var runner = new Runner(output);

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Commands = [command] },
            request: new() { ProjectRoot = Path.GetTempPath() });

        Assert.Equal(expectedValid, report.Success);
        Assert.Equal(1, runner.Calls);
        if (expectedValid) Assert.Equal("JSON casing probe", Assert.Single(report.Checks));
        else { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
    }

    private static void AssertSchemaValidity(ReleaseCommandValidation command, bool expectedValid)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(FindSchema()));
        var document = JsonNode.Parse(ReleaseValidationService.Serialize(new() { Commands = [command] }))!;
        var result = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.Equal(expectedValid, result.IsValid);
    }

    private static string FindSchema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "Schemas", "powerforge.release-validation.schema.json");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Release validation schema was not found above the test output directory.");
    }

    private sealed class Runner(string output) : IProcessRunner
    {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ProcessRunResult(0, output, "", "probe", TimeSpan.Zero, false));
        }
    }
}
