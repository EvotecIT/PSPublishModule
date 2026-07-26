using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellRunnerCancellationTests
{
    [Fact]
    public async Task RunAsync_forwards_cancellation_to_the_child_process_runner()
    {
        var executable = Path.GetTempFileName();
        try
        {
            var processRunner = new BlockingProcessRunner();
            var runner = (ICancellablePowerShellRunner)new PowerShellRunner(processRunner);
            var request = PowerShellRunRequest.ForCommand(
                commandText: "Write-Output 'test'",
                timeout: TimeSpan.FromMinutes(5),
                executableOverride: executable);
            using var cancellation = new CancellationTokenSource();

            var execution = runner.RunAsync(request, cancellation.Token);
            await processRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.True(processRunner.ObservedCancellation);
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public async Task RunAsync_rethrows_caller_cancellation_when_process_runner_returns_a_result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = (ICancellablePowerShellRunner)new PowerShellRunner(new CancellationSwallowingProcessRunner());
        var request = PowerShellRunRequest.ForCommand(
            commandText: "$null",
            timeout: TimeSpan.FromMinutes(1),
            executableOverride: Environment.ProcessPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(request, cancellation.Token));
    }

    private sealed class CancellationSwallowingProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ProcessRunResult(
                -1,
                string.Empty,
                "cancelled",
                request.FileName,
                TimeSpan.Zero,
                timedOut: false));
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool ObservedCancellation { get; private set; }

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            throw new InvalidOperationException("The blocking process runner unexpectedly completed.");
        }
    }
}
