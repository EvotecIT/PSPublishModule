using System.Diagnostics;

namespace PowerForge;

public sealed partial class ProcessRunner
{
    private static async Task<bool> WaitForOutputDrainAsync(Task<CapturedOutput> stdout,
        Task<CapturedOutput> stderr, TimeSpan timeout, TimeSpan elapsed, CancellationToken cancellationToken)
    {
        var drain = Task.WhenAll(stdout, stderr);
        // Readers may fault when cancellation closes the pipe after the main process exits.
        // Observe that fault even when the bounded caller has already returned.
        _ = drain.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (cancellationToken.IsCancellationRequested) return false;
        if (drain.IsCompleted) return true;
        var remaining = timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan
            ? timeout - elapsed : Timeout.InfiniteTimeSpan;
        if (remaining != Timeout.InfiniteTimeSpan && remaining <= TimeSpan.Zero) return false;
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = Task.Delay(remaining, delayCancellation.Token);
        var completed = await Task.WhenAny(drain, deadline).ConfigureAwait(false);
        delayCancellation.Cancel();
        return completed == drain && !cancellationToken.IsCancellationRequested;
    }

    private static void CloseCapturedStreams(Process process, ProcessRunRequest request)
    {
        try { if (request.CaptureOutput) process.StandardOutput.Dispose(); }
        catch (IOException) { }
        try { if (request.CaptureError) process.StandardError.Dispose(); }
        catch (IOException) { }
    }
}
