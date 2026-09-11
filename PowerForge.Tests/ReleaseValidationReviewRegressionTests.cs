using System.Diagnostics;
using System.IO.Compression;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge.Tests;

public sealed class ReleaseValidationReviewRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ReviewRegression.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationReviewRegressionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Inferred_tool_version_reaches_subsequent_top_level_commands()
    {
        using (var archive = ZipFile.Open(Path.Combine(_root, "Example.Tool.1.2.3.nupkg"), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Example.Tool.nuspec").Open()))
                writer.Write("<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
            using var payload = new StreamWriter(archive.CreateEntry("tools/net10.0/any/Example.dll").Open());
            payload.Write("fixture");
        }
        var requests = new List<ProcessRunRequest>();
        var runner = new RecordingRunner(request =>
        {
            requests.Add(request);
            return new(0, request.FileName == "probe" ? "1.2.3" : "", "", request.FileName, TimeSpan.Zero, false);
        });

        var report = await new ReleaseValidationService(runner).RunAsync(new()
        {
            Tools = [new() { PackageId = "Example.Tool", PackageRoot = _root, CommandName = "example" }],
            Commands = [new() { Name = "Version propagation", FileName = "probe", Arguments = ["{Version}"], ExpectedOutput = "{Version}" }]
        }, request: new() { ProjectRoot = _root });

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal("1.2.3", report.Version);
        Assert.Equal(new[] { "1.2.3" }, Assert.Single(requests, request => request.FileName == "probe").Arguments);
        Assert.Contains("Version propagation", report.Checks);
        Assert.False(Directory.Exists(requests[0].WorkingDirectory));
    }

    [Fact]
    public void IncludeSchema_emits_the_existing_hyphenated_schema_reference()
    {
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("New-ConfigurationReleaseValidation", typeof(PSPublishModule.NewConfigurationReleaseValidationCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using var shell = PowerShell.Create(runspace);
        shell.AddCommand("New-ConfigurationReleaseValidation").AddParameter("IncludeSchema");

        var output = shell.Invoke();

        Assert.Empty(shell.Streams.Error);
        var spec = Assert.IsType<ReleaseValidationSpec>(Assert.Single(output).BaseObject);
        Assert.Equal("https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release-validation.schema.json", spec.Schema);
        Assert.True(File.Exists(Path.Combine(FindRepository(), "Schemas", new Uri(spec.Schema!).Segments.Last())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_signing_timeout_preserves_configured_enablement_and_timeout_in_real_release_plan(bool enabled)
    {
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>1.2.3</Version></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_root, "Installer.wixproj"), "<Project />");
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Unexpected package execution"),
            planTools: (_, _, _) => throw new InvalidOperationException("Unexpected legacy tool plan"),
            runTools: _ => throw new InvalidOperationException("Unexpected tool execution"),
            publishGitHubRelease: _ => throw new InvalidOperationException("Unexpected publication"));

        var result = service.Execute(new()
        {
            Tools = new()
            {
                DotNetPublish = new()
                {
                    DotNet = new() { ProjectRoot = "." },
                    Targets = [new() { Name = "app", ProjectPath = "App.csproj", Publish = new()
                    {
                        Framework = "net10.0", Runtimes = ["win-x64"], Style = DotNetPublishStyle.PortableCompat,
                        Sign = new() { Enabled = enabled, TimeoutSeconds = 77 }
                    } }],
                    Installers = [new() { Id = "app.msi", PrepareFromTarget = "app", InstallerProjectPath = "Installer.wixproj",
                        Sign = new() { Enabled = enabled, TimeoutSeconds = 88 } }]
                }
            }
        }, new() { ConfigPath = Path.Combine(_root, "release.json"), PlanOnly = true, ToolsOnly = true, SignTimeoutSeconds = 0 });

        Assert.True(result.Success);
        Assert.NotNull(result.DotNetToolPlan);
        var target = Assert.Single(result.DotNetToolPlan.Targets);
        Assert.NotNull(target.Publish.Sign);
        Assert.Equal(enabled, target.Publish.Sign.Enabled);
        Assert.Equal(77, target.Publish.Sign.TimeoutSeconds);
        var installer = Assert.Single(result.DotNetToolPlan.Installers);
        Assert.NotNull(installer.Sign);
        Assert.Equal(enabled, installer.Sign.Enabled);
        Assert.Equal(88, installer.Sign.TimeoutSeconds);
    }

    [ReleaseValidationLinuxFact]
    public async Task Inherited_pipe_drain_observes_timeout_after_parent_exit()
    {
        var pidFile = Path.Combine(_root, "child.pid");
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = ShellRequest(pidFile, TimeSpan.FromMilliseconds(500));
        request.SetCompletionBoundary(_ => exited.TrySetResult());
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var run = new ProcessRunner().RunAsync(request);
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await run.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(result.TimedOut);
            Assert.False(result.Succeeded);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Drain took {stopwatch.Elapsed}.");
            Assert.True(File.Exists(pidFile));
        }
        finally { await KillChildAsync(pidFile); }
    }

    [ReleaseValidationLinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inherited_pipe_drain_observes_caller_cancellation(bool throughReleaseCommand)
    {
        var pidFile = Path.Combine(_root, "child.pid");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (throughReleaseCommand)
            {
                var command = new ReleaseCommandValidation
                {
                    FileName = "/bin/sh", WorkingDirectory = _root, Arguments = ShellArguments,
                    TimeoutSeconds = 20, Environment = new() { ["CHILD_PID_FILE"] = pidFile }
                };
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new ReleaseValidationService().RunCommandAsync(command, cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(3)));
            }
            else
            {
                var result = await new ProcessRunner().RunAsync(ShellRequest(pidFile, TimeSpan.FromSeconds(20)), cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(cancellation.IsCancellationRequested);
                Assert.False(result.TimedOut);
            }
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Cancellation took {stopwatch.Elapsed}.");
            Assert.True(File.Exists(pidFile));
        }
        finally { await KillChildAsync(pidFile); }
    }

    private static string[] ShellArguments => ["-c", "sleep 10 & echo $! > \"$CHILD_PID_FILE\""];
    private ProcessRunRequest ShellRequest(string pidFile, TimeSpan timeout) => new("/bin/sh", _root, ShellArguments,
        timeout, new Dictionary<string, string?> { ["CHILD_PID_FILE"] = pidFile });

    private static async Task KillChildAsync(string pidFile)
    {
        if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid)) return;
        try
        {
            using var child = Process.GetProcessById(pid);
            if (!child.HasExited && child.ProcessName == "sleep")
            {
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    private sealed class RecordingRunner(Func<ProcessRunRequest, ProcessRunResult> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(execute(request));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

public sealed class ReleaseValidationLinuxFactAttribute : FactAttribute
{
    public ReleaseValidationLinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires Linux inherited anonymous pipes."; }
}

public sealed class ReleaseValidationLinuxTheoryAttribute : TheoryAttribute
{
    public ReleaseValidationLinuxTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires Linux inherited anonymous pipes."; }
}
