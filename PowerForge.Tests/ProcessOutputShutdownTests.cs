using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class ProcessOutputShutdownTests
{
    [Fact]
    public async Task Failed_completion_boundary_joins_callback_and_preserves_original_exception()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var boundaryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("producer output changed");
        var finished = false;
        var request = new ProcessRunRequest("pwsh", Path.GetTempPath(),
            new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('captured')" },
            TimeSpan.FromSeconds(30), environmentVariables: null, captureOutput: true, captureError: true,
            outputLineReceived: _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); finished = true; },
            errorLineReceived: null);
        request.SetCompletionBoundary(_ =>
        {
            if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Callback did not start.");
            boundaryEntered.SetResult();
            throw expected;
        });
        var run = new ProcessRunner().RunAsync(request);
        try
        {
            await boundaryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(TimeSpan.FromMilliseconds(200)));
            Assert.False(finished);
        }
        finally { release.Set(); }
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => run));
        Assert.True(finished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_joins_inflight_callbacks_before_returning(bool ownProcessTree)
    {
        using var releaseCallback = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = false;
        var request = new ProcessRunRequest("pwsh", Path.GetTempPath(),
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('captured')" },
            TimeSpan.FromSeconds(30), environmentVariables: null, captureOutput: true, captureError: true, outputLineReceived: _ =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Wait(TimeSpan.FromSeconds(10));
                callbackFinished = true;
            }, errorLineReceived: null);
        request.SetCompletionBoundary(_ => producerCompleted.TrySetResult());
        var run = new ProcessRunner(ownProcessTree).RunAsync(request, cancellation.Token);
        try
        {
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await producerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(TimeSpan.FromMilliseconds(200)));
            Assert.False(callbackFinished);
        }
        finally
        {
            releaseCallback.Set();
        }
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(callbackFinished);
        Assert.Equal(130, result.ExitCode);
        Assert.Contains("captured", result.StdOut);
    }

    [Fact]
    public async Task Reader_fault_is_not_complete_until_final_partial_line_callback_finishes()
    {
        using var releaseCallback = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reader = new StreamReader(new FaultAfterPayloadStream(), Encoding.UTF8);
        var capture = RedirectedProcessOutput.Start(reader, lineReceived: _ =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Wait(TimeSpan.FromSeconds(10));
        });
        try
        {
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(capture.Completion.IsCompleted);
        }
        finally { releaseCallback.Set(); }
        await Assert.ThrowsAsync<NotSupportedException>(() => capture.Completion);
        Assert.Equal("partial", capture.Snapshot());
    }

    [Fact]
    public async Task Deadline_cancels_reader_of_pipe_inherited_by_unowned_child()
    {
        var childId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = OperatingSystem.IsWindows();
        var executable = windows ? "pwsh" : "/bin/sh";
        var arguments = windows
            ? new[] { "-NoProfile", "-NonInteractive", "-Command",
                "$child = Start-Process (Join-Path $PSHOME 'pwsh.exe') -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 30' -NoNewWindow -PassThru; [Console]::WriteLine($child.Id)" }
            : new[] { "-c", "sleep 30 & echo $!" };
        var request = new ProcessRunRequest(executable, Path.GetTempPath(), arguments, TimeSpan.FromSeconds(3),
            environmentVariables: null, captureOutput: true, captureError: true,
            outputLineReceived: line => { if (int.TryParse(line, out var id)) childId.TrySetResult(id); }, errorLineReceived: null);
        var run = new ProcessRunner().RunAsync(request);
        var id = await childId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.TimedOut);
            Assert.Equal(124, result.ExitCode);
            Assert.Equal(id.ToString(), result.StdOut.Trim());
        }
        finally
        {
            try { using var child = Process.GetProcessById(id); child.Kill(); }
            catch (ArgumentException) { }
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class FaultAfterPayloadStream : MemoryStream
    {
        private bool _read;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read) throw new NotSupportedException("broken transport");
            _read = true;
            var payload = Encoding.UTF8.GetBytes("partial");
            Array.Copy(payload, 0, buffer, offset, payload.Length);
            return Task.FromResult(payload.Length);
        }
    }
}
