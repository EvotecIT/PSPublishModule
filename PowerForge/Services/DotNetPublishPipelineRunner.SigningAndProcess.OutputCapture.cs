using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void DrainRedirectedOutputReads(
        Process process,
        Task stdoutRead,
        Task stderrRead,
        TimeSpan timeout)
    {
        var reads = Task.WhenAll(stdoutRead, stderrRead);
        try
        {
            if (reads.Wait(timeout))
                return;
        }
        catch (AggregateException)
        {
            return;
        }

        if (!stdoutRead.IsCompleted)
            process.StandardOutput.Dispose();
        if (!stderrRead.IsCompleted)
            process.StandardError.Dispose();
        try
        {
            reads.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // A disposed redirected stream can fault its outstanding read.
        }
    }

}
