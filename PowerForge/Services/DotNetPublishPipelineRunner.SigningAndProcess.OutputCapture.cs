using System.Diagnostics;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class RedirectedOutputCapture
    {
        private readonly object _sync = new();
        private readonly StringBuilder _output = new();

        internal void Append(char[] buffer, int count)
        {
            lock (_sync)
                _output.Append(buffer, 0, count);
        }

        internal string Snapshot()
        {
            lock (_sync)
                return _output.ToString();
        }
    }

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

    private static Task ReadRedirectedOutputAsync(
        StreamReader reader,
        RedirectedOutputCapture capture)
        => Task.Run(async () =>
        {
            var buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    capture.Append(buffer, count);
            }
            catch (IOException)
            {
                // Disposing an inherited redirected stream ends the bounded read.
            }
            catch (ObjectDisposedException)
            {
                // Disposing an inherited redirected stream ends the bounded read.
            }
            catch (InvalidOperationException)
            {
                // The asynchronous reader can report disposal as an invalid operation.
            }
        });
}
