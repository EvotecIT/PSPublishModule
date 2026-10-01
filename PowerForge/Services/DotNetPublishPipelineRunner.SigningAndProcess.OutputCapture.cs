namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void DrainRedirectedOutputReads(
        RedirectedProcessOutput stdout,
        RedirectedProcessOutput stderr,
        TimeSpan timeout)
    {
        try
        {
            if (Task.WaitAll(new[] { stdout.Completion, stderr.Completion }, timeout))
                return;
        }
        catch (AggregateException)
        {
            return;
        }

        try
        {
            StopRedirectedOutputReads(stdout, stderr);
        }
        catch (IOException)
        {
            // A disposed redirected stream can fault its outstanding read.
        }
    }

    private static void StopRedirectedOutputReads(RedirectedProcessOutput stdout, RedirectedProcessOutput stderr)
    {
        var outputStop = stdout.StopAsync();
        var errorStop = stderr.StopAsync();
        // Wait directly on the readers: a WhenAll continuation can itself be queued
        // behind blocked process callers on a saturated worker pool.
        try { Task.WaitAll(outputStop, errorStop); }
        catch (AggregateException)
        {
            // Both readers have finished; retain the previous unwrapped fault contract.
            Task.WhenAll(outputStop, errorStop).GetAwaiter().GetResult();
        }
    }

}
