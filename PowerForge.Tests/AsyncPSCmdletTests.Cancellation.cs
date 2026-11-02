using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed partial class AsyncPSCmdletTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsyncPSCmdlet_bounds_stop_cleanup_and_drains_cooperative_hooks(bool finishesDuringGrace)
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCancellationCleanup", typeof(TestAsyncCancellationCleanupCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var shell = PowerShell.Create();
        shell.Runspace = runspace;
        shell.AddCommand("Test-AsyncCancellationCleanup");
        TestAsyncCancellationCleanupCommand.Reset();
        var invocation = shell.BeginInvoke();
        Task? stop = null;
        try
        {
            Assert.True(TestAsyncCancellationCleanupCommand.Started.Wait(TimeSpan.FromSeconds(5)));
            stop = Task.Run(shell.Stop);
            Assert.True(TestAsyncCancellationCleanupCommand.CleanupStarted.Wait(TimeSpan.FromSeconds(5)));
            if (finishesDuringGrace)
            {
                Assert.NotSame(stop, await Task.WhenAny(stop, Task.Delay(100)));
                TestAsyncCancellationCleanupCommand.AllowCleanup();
                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(TestAsyncCancellationCleanupCommand.CleanupCompleted);
            }
            else
            {
                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(TestAsyncCancellationCleanupCommand.CleanupCompleted);
            }
        }
        finally
        {
            TestAsyncCancellationCleanupCommand.AllowCleanup();
            if (stop is not null) await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await TestAsyncCancellationCleanupCommand.CleanupFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<PipelineStoppedException>(() => shell.EndInvoke(invocation));
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCancellationCleanup")]
public sealed class TestAsyncCancellationCleanupCommand : AsyncPSCmdlet
{
    private static TaskCompletionSource<bool> _allowCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static ManualResetEventSlim Started { get; } = new();
    public static ManualResetEventSlim CleanupStarted { get; } = new();
    public static bool CleanupCompleted { get; private set; }
    public static TaskCompletionSource<bool> CleanupFinished { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset()
    {
        Started.Reset();
        CleanupStarted.Reset();
        CleanupCompleted = false;
        CleanupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _allowCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static void AllowCleanup() => _allowCleanup.TrySetResult(true);

    protected override async Task ProcessRecordAsync()
    {
        Started.Set();
        try { await Task.Delay(Timeout.Infinite, CancelToken); }
        finally
        {
            CleanupStarted.Set();
            await _allowCleanup.Task;
            CleanupCompleted = true;
            CleanupFinished.SetResult(true);
        }
    }
}
