using System.Text;

namespace PowerForge.Tests;

public sealed class ModuleRepositoryRetryPolicyTests
{
    [Fact]
    public void Child_install_recovers_from_encoded_404_then_success_and_logs_backoff()
    {
        var logger = new RecordingLogger();
        var waits = new List<TimeSpan>();
        var calls = 0;
        var result = new ModuleRepositoryRetryPolicy(logger, waits.Add).RunPowerShell("Install Pester 6.1.0", () =>
            ++calls == 1 ? Error("Response status code does not indicate success: 404 (Not Found).") : Success());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, waits);
        Assert.Contains(logger.Messages, message => message.Contains("attempt 1/4"));
        Assert.Contains(logger.Messages, message => message.Contains("attempt 2/4"));
    }

    [Fact]
    public void Permanent_gallery_404_stops_with_actionable_error_after_bounded_attempts()
    {
        var waits = new List<TimeSpan>();
        var calls = 0;
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ModuleRepositoryRetryPolicy(new NullLogger(), waits.Add).RunPowerShell("Save Pester 6.1.0", () =>
            {
                calls++;
                return Error("HTTP 404 Not Found");
            }));

        Assert.Equal(4, calls);
        Assert.Equal(new[] { 1d, 2d, 4d }, waits.Select(wait => wait.TotalSeconds));
        Assert.Contains("Pester 6.1.0", exception.Message);
        Assert.Contains("after 4 attempts", exception.Message);
        Assert.Contains("requested version", exception.Message);
        Assert.Contains("404", exception.Message);
    }

    [Theory]
    [InlineData("HTTP 429 Too Many Requests", true)]
    [InlineData("HTTP 503 Service Unavailable", true)]
    [InlineData("The operation has timed out.", true)]
    [InlineData("HTTP 401 Unauthorized", false)]
    [InlineData("HTTP 403 Forbidden", false)]
    [InlineData("No match was found for the specified search criteria.", false)]
    [InlineData("Invalid version range.", false)]
    [InlineData("Destination file not found: /tmp/modules/Pester.psd1", false)]
    public void Child_install_retries_only_transient_repository_failures(string message, bool transient)
    {
        Assert.Equal(transient, ModuleRepositoryRetryPolicy.IsTransientError(message));
    }

    [Fact]
    public void Missing_tools_and_caller_cancellation_are_not_retried()
    {
        var calls = 0;
        var policy = new ModuleRepositoryRetryPolicy(new NullLogger(), _ => throw new Xunit.Sdk.XunitException("Unexpected retry"));
        Assert.Equal(3, policy.RunPowerShell("Install", () => { calls++; return Error("404", 3); }).ExitCode);
        Assert.Equal(1, calls);
        Assert.Throws<OperationCanceledException>(() => policy.RunPowerShell("Install", () => throw new OperationCanceledException()));
    }

    [Fact]
    public void Timeouts_are_retried_and_the_final_error_names_the_operation()
    {
        var calls = 0;
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ModuleRepositoryRetryPolicy(new NullLogger(), _ => { }).RunPowerShell("Install Pester 6.1.0", () =>
            {
                calls++;
                throw new TimeoutException("Request timed out");
            }));
        Assert.Equal(4, calls);
        Assert.Contains("Install Pester 6.1.0", error.Message);
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PSResourceGet_install_and_save_apply_the_shared_retry_policy(bool save)
    {
        var calls = 0;
        var runner = new CallbackRunner(_ => ++calls == 1 ? Error("HTTP 404 Not Found") : Success());
        var client = new PSResourceGetClient(runner, new NullLogger());
        var destination = Path.Combine(Path.GetTempPath(), "pf-save-retry-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (save)
                client.Save(new PSResourceSaveOptions("Pester", destination, version: "6.1.0"));
            else
                client.Install(new PSResourceInstallOptions("Pester", version: "6.1.0"));
            Assert.Equal(2, calls);
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Find_retries_the_PSWriteColor_source_query_without_changing_its_options(bool permanent)
    {
        const string url = "https://www.powershellgallery.com/api/v2/FindPackagesById()?%24filter=Id+eq+%27PSWriteColor%27+and+IsLatestVersion+eq+true&%24inlinecount=allpages&id=%27PSWriteColor%27";
        var calls = 0;
        var runner = new CallbackRunner(request =>
        {
            Assert.Equal(string.Empty, request.Arguments[1]);
            calls++;
            if (permanent || calls == 1)
                return Error("'Response status code does not indicate success: 404 (Not Found).' Request sent: '" + url + "'");
            var fields = new[] { "PSWriteColor", "1.0.7", "PSGallery", "Author", "Description", "", "" };
            return new PowerShellRunResult(0, "PFPSRG::ITEM::" + string.Join("::", fields.Select(value =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(value)))), string.Empty, "pwsh");
        });
        var client = new PSResourceGetClient(runner, new NullLogger());
        if (permanent)
        {
            var error = Assert.Throws<InvalidOperationException>(() => client.Find(new PSResourceFindOptions(new[] { "PSWriteColor" })));
            Assert.Equal(4, calls);
            Assert.Contains(url, error.Message);
            Assert.Contains("after 4 attempts", error.Message);
        }
        else
        {
            var found = Assert.Single(client.Find(new PSResourceFindOptions(new[] { "PSWriteColor" })));
            Assert.Equal("PSWriteColor", found.Name);
            Assert.Equal("1.0.7", found.Version);
            Assert.Equal(2, calls);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(20, 30)]
    public void Backoff_is_exponential_and_capped(int attempt, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), ModuleRepositoryRetryPolicy.Backoff(TimeSpan.FromSeconds(1), attempt));

    private static PowerShellRunResult Success() => new(0, string.Empty, string.Empty, "pwsh");
    private static PowerShellRunResult Error(string message, int exitCode = 1)
        => new(exitCode, "PFPSRG::ERROR::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(message)), string.Empty, "pwsh");

    private sealed class CallbackRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }

    private sealed class RecordingLogger : ILogger
    {
        internal List<string> Messages { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Success(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
