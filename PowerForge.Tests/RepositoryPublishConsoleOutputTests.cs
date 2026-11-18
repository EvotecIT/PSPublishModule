using System.Collections.Concurrent;

namespace PowerForge.Tests;

public sealed class RepositoryPublishConsoleOutputTests
{
    [Theory]
    [InlineData(PublishTool.PSResourceGet, "Response status code does not indicate success: 403 (Forbidden).", "refused the upload (HTTP 403)")]
    [InlineData(PublishTool.PowerShellGet, "The specified API key is invalid, has expired, or does not have permission to access the specified package.", "rejected the API key")]
    [InlineData(PublishTool.PSResourceGet, "Response status code does not indicate success: 409 (Conflict - The feed already contains 'Fixture 1.0.0'.).", "version already exists")]
    [InlineData(PublishTool.PowerShellGet, "The module 'Fixture' with version '1.0.0' cannot be published as the current version '1.0.0' is already available in the repository.", "version already exists")]
    public void Publish_failure_explains_the_repository_answer(PublishTool tool, string publisherError, string explanation)
    {
        var error = RepositoryDiagnosticService.PublishFailure(tool, 1, publisherError, string.Empty, "PSGallery", "Fixture", "fixture-key", null);

        Assert.Contains(explanation, error.Message, StringComparison.Ordinal);
        Assert.Contains(publisherError, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PowerShellGet_publish_streams_child_warnings_reports_bootstrap_and_logs_the_failure_once(bool preferPwsh)
    {
        using var root = new TemporaryDirectory();
        var logger = new RecordingLogger();
        var client = new PowerShellGetClient(new FixtureRunner(root.Path, preferPwsh), logger);

        var error = Assert.Throws<RepositoryPublishDiagnosticException>(() =>
            client.Publish(new PowerShellGetPublishOptions(root.Path, "PSGallery", "fixture-key")));

        Assert.Contains(logger.Warnings, warning => warning.Contains("fixture-gallery-warning", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, info => info.Contains("Installing the NuGet package provider", StringComparison.Ordinal));
        Assert.Contains("version already exists", error.Message, StringComparison.Ordinal);
        Assert.Contains("is already available in the repository", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-key", error.Message, StringComparison.Ordinal);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Auto_publish_warns_when_it_falls_back_to_PowerShellGet()
    {
        using var root = new TemporaryDirectory();
        var logger = new RecordingLogger();
        var runner = new BackendRunner();

        new RepositoryPublisher(logger, runner).Publish(new RepositoryPublishRequest
        {
            Path = root.Path,
            RepositoryName = "PSGallery",
            Tool = PublishTool.Auto,
            ApiKey = "fixture-key"
        });

        Assert.Contains(logger.Warnings, warning =>
            warning.Contains("falls back to PowerShellGet", StringComparison.Ordinal) &&
            warning.Contains("PSResourceGet missing in fixture", StringComparison.Ordinal));
        Assert.Contains(runner.Scripts, script => script.Contains("Publish-Module @params", StringComparison.Ordinal));
    }

    private sealed class FixtureRunner(string root, bool preferPwsh) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            // Replace only the PowerShellGet and PackageManagement commands. The composed script,
            // child process and live line streaming are real.
            const string fixture = """
                function Import-Module { param($Name, $ErrorAction) }
                function Get-Module { param($Name, [switch]$ListAvailable) }
                function Get-PackageProvider { param($Name, [switch]$ListAvailable, $ErrorAction, $WarningAction) }
                function Install-PackageProvider { param($Name, $MinimumVersion, $Scope, [switch]$Force, $ErrorAction) }
                function Import-PackageProvider { param($Name, $MinimumVersion, [switch]$Force, $ErrorAction) }
                function Publish-Module {
                    Write-Warning 'fixture-gallery-warning'
                    throw "The module 'Fixture' with version '1.0.0' cannot be published as the current version '1.0.0' is already available in the repository."
                }
                """;
            var script = File.ReadAllText(request.ScriptPath!);
            var scriptPath = Path.Combine(root, "publish-fixture.ps1");
            File.WriteAllText(scriptPath, script.Replace("$ProgressPreference = 'SilentlyContinue'",
                "$ProgressPreference = 'SilentlyContinue'\n" + fixture), new System.Text.UTF8Encoding(true));
            return new PowerShellRunner().Run(new PowerShellRunRequest(scriptPath, request.Arguments, TimeSpan.FromSeconds(60),
                preferPwsh, workingDirectory: root, environmentVariables: null, executableOverride: null,
                captureOutput: true, captureError: true, request.OutputLineReceived, request.ErrorLineReceived));
        }
    }

    private sealed class BackendRunner : IPowerShellRunner
    {
        internal ConcurrentQueue<string> Scripts { get; } = new();

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var script = File.ReadAllText(request.ScriptPath!);
            Scripts.Enqueue(script);
            if (script.Contains("Publish-PSResource @params", StringComparison.Ordinal))
                return new PowerShellRunResult(3, "PFPSRG::ERROR::" + Encode("PSResourceGet missing in fixture"), string.Empty, "pwsh");
            return new PowerShellRunResult(0, "PFPWSGET::PUBLISH::OK", string.Empty, "pwsh");
        }

        private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
    }

    private sealed class RecordingLogger : ILogger
    {
        internal ConcurrentQueue<string> Infos { get; } = new();
        internal ConcurrentQueue<string> Warnings { get; } = new();
        internal ConcurrentQueue<string> Errors { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) => Infos.Enqueue(message);
        public void Success(string message) => Infos.Enqueue(message);
        public void Warn(string message) => Warnings.Enqueue(message);
        public void Error(string message) => Errors.Enqueue(message);
        public void Verbose(string message) { }
    }
}
