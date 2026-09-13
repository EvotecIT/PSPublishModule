using System.Text;

namespace PowerForge.Tests;

public sealed class ReleaseValidationConfigurationLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ConfigurationLoading.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationConfigurationLoadingTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public async Task Async_and_synchronous_loaders_preserve_BOM_encoded_contracts(string encodingName)
    {
        var path = Path.Combine(_root, "validation.json");
        var encoding = encodingName switch { "utf16" => Encoding.Unicode, "utf32" => Encoding.UTF32, _ => new UTF8Encoding(true) };
        File.WriteAllText(path, "{\"Commands\":[{\"Name\":\"Żółć\",\"FileName\":\"probe\",\"Arguments\":[\"{Package_Root}\"]}]}", encoding);

        var spec = await ReleaseValidationService.LoadAsync(path);

        var command = Assert.Single(spec.Commands);
        Assert.Equal("Żółć", command.Name);
        Assert.Equal("probe", command.FileName);
        Assert.Equal(new[] { "{Package_Root}" }, command.Arguments);
        Assert.Equal(ReleaseValidationService.Serialize(spec), ReleaseValidationService.Serialize(ReleaseValidationService.Load(path)));
    }

    [Fact]
    public async Task Cancelled_load_does_not_open_or_parse_the_configuration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(_root, "not-present.json");

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReleaseValidationService.LoadAsync(path, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Async_loader_retains_configuration_size_bound()
    {
        var path = Path.Combine(_root, "oversized.json");
        using (var file = File.Create(path)) { file.SetLength(DotNetPublishReleaseArtifactVerifier.MaxConfigurationBytes + 1); }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseValidationService.LoadAsync(path));

        Assert.Contains("byte limit", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_deadline_includes_preparation_before_configuration_or_script_execution(bool script)
    {
        var path = Write(script ? "probe.ps1" : "validation.json", script ? "'probe'" : "{\"Commands\":[{\"FileName\":\"probe\"}]}");
        var runner = new Runner();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new CallbackLogger(() => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException("Test preparation gate was not released."); } });
        var service = new PowerForgeReleaseValidationService(logger, runner);
        var action = new PowerForgeReleaseValidationAction { Name = "Whole action", TimeoutSeconds = 1 };
        if (script) { action.FilePath = path; } else { action.ConfigPath = path; }
        var task = Task.Run(() => service.Run(action, new(), _root, CancellationToken.None));
        try {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Action did not reach preparation.");
            // Hold the existing logging boundary beyond the configured deadline, before the file is loaded.
            await Task.Delay(TimeSpan.FromSeconds(2));
        } finally { release.Set(); }

        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("Whole action", result.Name);
        Assert.Equal(path, result.FilePath);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Caller_cancellation_during_preparation_is_not_reported_as_timeout(bool script)
    {
        var path = Write(script ? "probe.ps1" : "validation.json", script ? "'probe'" : "invalid JSON that must not be parsed");
        using var cancellation = new CancellationTokenSource();
        var runner = new Runner();
        var service = new PowerForgeReleaseValidationService(new CallbackLogger(cancellation.Cancel), runner);
        var action = new PowerForgeReleaseValidationAction();
        if (script) { action.FilePath = path; } else { action.ConfigPath = path; }

        Assert.ThrowsAny<OperationCanceledException>(() => service.Run(action, new(), _root, cancellation.Token));

        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void Unrelated_cancellation_is_not_misclassified_as_an_action_timeout()
    {
        var path = Write("validation.json", "{\"Commands\":[{\"FileName\":\"probe\"}]}");
        var runner = new Runner { ThrowCancellation = true };

        Assert.ThrowsAny<OperationCanceledException>(() => new PowerForgeReleaseValidationService(new NullLogger(), runner)
            .Run(new() { ConfigPath = path, TimeoutSeconds = 30 }, new(), _root, CancellationToken.None));

        Assert.Equal(1, runner.Calls);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    private sealed class Runner : IProcessRunner
    {
        internal int Calls { get; private set; }
        internal bool ThrowCancellation { get; set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowCancellation) { throw new OperationCanceledException("Independent cancellation"); }
            return Task.FromResult(new ProcessRunResult(0, "observed", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    private sealed class CallbackLogger(Action callback) : ILogger
    {
        public bool IsVerbose => false;
        public void Info(string message) => callback();
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
