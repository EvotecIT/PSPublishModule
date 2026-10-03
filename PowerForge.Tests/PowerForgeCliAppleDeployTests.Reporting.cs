using System.Reflection;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliAppleDeployTests
{
    [Fact]
    public void AppleDeploy_diagnostic_keeps_both_output_streams_and_reports_timeout()
    {
        var failure = new ProcessRunResult(65,
            "Compile.swift: error: invalid expression\n" + new string('x', 3000),
            "warning: unrelated signing warning\nsecret-key", "xcodebuild", TimeSpan.FromSeconds(4), timedOut: true);
        var diagnostic = (string)InvokeAppleDeployReporting("ResolveAppleDeployDiagnostic",
            new[] { "secret-key" }, new ProcessRunResult?[] { failure })!;
        Assert.Contains("timed out", diagnostic);
        Assert.Contains("Compile.swift: error: invalid expression", diagnostic);
        Assert.Contains("unrelated signing warning", diagnostic);
        Assert.DoesNotContain("secret-key", diagnostic);
        Assert.Contains("[REDACTED]", diagnostic);
        Assert.True(diagnostic.Length <= 2000);
    }

    [Fact]
    public void AppleDeploy_console_reports_install_warning_failed_stage_and_timings()
    {
        var program = GetAppleDeployProgram();
        var resultType = program.Assembly.GetType("PowerForge.Cli.AppleLocalDeploymentCliResult", throwOnError: true)!;
        var result = Activator.CreateInstance(resultType)!;
        resultType.GetProperty("Warning")!.SetValue(result, "Rollback copy could not be removed.");
        resultType.GetProperty("Diagnostic")!.SetValue(result, "Install failed: invalid signature.");
        var build = new ProcessRunResult(0, "ok", "", "xcodebuild", TimeSpan.FromSeconds(12.5), false);
        var install = new ProcessRunResult(1, "", "invalid signature", "xcrun", TimeSpan.FromSeconds(3), false);
        var stages = InvokeAppleDeployReporting("CollectAppleDeployStages", build, install, null);
        resultType.GetProperty("Stages")!.SetValue(result, stages);
        var logger = new AppleDeployRecordingLogger();

        var exitCode = (int)InvokeAppleDeployReporting("WriteAppleDeployResult", result, "release.json", false, logger)!;

        Assert.Equal(1, exitCode);
        Assert.Contains("Rollback copy could not be removed.", logger.Warnings);
        Assert.Contains("Install failed: invalid signature.", logger.Errors);
        Assert.Contains(logger.InfoMessages, message => message.StartsWith("Build: ") && message.Contains("(exit 0)"));
        Assert.Contains(logger.InfoMessages, message => message.StartsWith("Install: ") && message.Contains("(exit 1)"));
        Assert.DoesNotContain(logger.InfoMessages, message => message.StartsWith("Launch: "));
        var first = ((Array)stages!).GetValue(0)!;
        Assert.Equal(12.5, first.GetType().GetProperty("DurationSeconds")!.GetValue(first));
    }

    [Fact]
    public async Task AppleDeploy_plan_requires_device_selection_before_source_inspection()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Sample.xcodeproj"));
        try
        {
            var config = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(config, """
                { "SchemaVersion": 1, "AppleApps": { "Apps": [
                  { "Name": "Sample", "Platform": "iOS", "Scheme": "Sample", "ProjectPath": "Sample.xcodeproj" }
                ] } }
                """);
            var result = await RunCliAsync(FindRepositoryRoot(),
                $"\"{GetCliPath(FindRepositoryRoot())}\" apple-deploy --config \"{config}\" --plan --output json");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Device deployment requires", result.StdOut + result.StdErr);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Type GetAppleDeployProgram()
        => Assembly.LoadFrom(GetCliPath(FindRepositoryRoot())).GetType("Program", throwOnError: true)!;

    private static object? InvokeAppleDeployReporting(string method, params object?[] arguments)
        => GetAppleDeployProgram().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);

    private sealed class AppleDeployRecordingLogger : ILogger
    {
        public List<string> InfoMessages { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) => InfoMessages.Add(message);
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) => Errors.Add(message);
        public void Success(string message) { }
        public void Verbose(string message) { }
    }
}
