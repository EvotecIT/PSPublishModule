namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void DrainRedirectedOutputReads(
        RedirectedProcessOutput stdout,
        RedirectedProcessOutput stderr,
        TimeSpan timeout)
    {
        var reads = Task.WhenAll(stdout.Completion, stderr.Completion);
        try
        {
            if (reads.Wait(timeout))
                return;
        }
        catch (AggregateException)
        {
            return;
        }

        try
        {
            Task.WhenAll(stdout.StopAsync(), stderr.StopAsync()).GetAwaiter().GetResult();
        }
        catch (IOException)
        {
            // A disposed redirected stream can fault its outstanding read.
        }
    }

}
