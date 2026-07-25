namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerCancellationTests
{
    [Fact]
    public async Task Run_cancels_the_active_dotnet_process()
    {
        using var scope = new CancellationTokenSource();
        var processRunner = new WaitingProcessRunner();
        var runner = new DotNetPublishPipelineRunner(new NullLogger(), processRunner);
        var plan = new DotNetPublishPlan
        {
            ProjectRoot = Directory.GetCurrentDirectory(),
            SolutionPath = "Sample.sln",
            Steps =
            [
                new DotNetPublishStep
                {
                    Key = "restore",
                    Kind = DotNetPublishStepKind.Restore,
                    Title = "Restore"
                }
            ]
        };

        var execution = Task.Run(() => runner.Run(plan, progress: null, scope.Token));
        await processRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(processRunner.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task Run_cancels_the_active_command_hook_process()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var startedPath = Path.Combine(root, "hook-started.txt");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "hook:BeforeBuild:cancellation",
                        Kind = DotNetPublishStepKind.CommandHook,
                        HookId = "cancellation",
                        HookPhase = DotNetPublishCommandHookPhase.BeforeBuild,
                        HookCommand = "pwsh",
                        HookArguments =
                        [
                            "-NoLogo",
                            "-NoProfile",
                            "-Command",
                            "Set-Content -LiteralPath $env:PF_HOOK_STARTED -Value started; Start-Sleep -Seconds 60"
                        ],
                        HookEnvironment = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["PF_HOOK_STARTED"] = startedPath
                        },
                        HookTimeoutSeconds = 90,
                        HookRequired = true
                    }
                ]
            };
            using var cancellation = new CancellationTokenSource();
            var execution = Task.Run(() =>
                new DotNetPublishPipelineRunner(new NullLogger())
                    .Run(plan, progress: null, cancellation.Token));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (!File.Exists(startedPath) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50);
            Assert.True(File.Exists(startedPath), "The command hook did not start.");

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class WaitingProcessRunner : IProcessRunner
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CancellationToken ObservedToken { get; private set; }

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The process runner was not cancelled.");
        }
    }
}
