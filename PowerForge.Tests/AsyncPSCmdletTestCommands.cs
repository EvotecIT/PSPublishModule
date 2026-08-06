using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using PSPublishModule;

namespace PowerForge.Tests;

[Cmdlet(VerbsDiagnostic.Test, "AsyncThreadAffinity")]
public sealed class TestAsyncThreadAffinityCommand : AsyncPSCmdlet
{
    private int _pipelineThreadId;

    protected override void BeginProcessing()
    {
        _pipelineThreadId = Environment.CurrentManagedThreadId;
        base.BeginProcessing();
    }

    protected override async Task ProcessRecordAsync()
    {
        Assert.Equal(_pipelineThreadId, Environment.CurrentManagedThreadId);
        await Task.Yield();
        WriteObject("post-await-output");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncQueuedOutput")]
public sealed class TestAsyncQueuedOutputCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        using var ready = new ManualResetEventSlim();
        Exception? workerException = null;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                WriteObject("queued-output");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
            finally
            {
                ready.Set();
            }
        });

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)), "Worker thread did not write output in time.");
        if (workerException is not null)
            throw workerException;

        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronizationContext")]
public sealed class TestAsyncSynchronizationContextCommand : AsyncPSCmdlet
{
    private ForwardingSynchronizationContext? _context;

    protected override void ProcessRecord()
    {
        var previousContext = SynchronizationContext.Current;
        _context = new ForwardingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            base.ProcessRecord();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteObject(_context!.PostCount);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncTaskScheduler")]
public sealed class TestAsyncTaskSchedulerCommand : AsyncPSCmdlet
{
    private readonly ForwardingTaskScheduler _scheduler = new();
    private int _pipelineThreadId;

    protected override void ProcessRecord()
    {
        _pipelineThreadId = Environment.CurrentManagedThreadId;
        _scheduler.Run(base.ProcessRecord);
    }

    protected override async Task ProcessRecordAsync()
    {
        Assert.Equal(_pipelineThreadId, Environment.CurrentManagedThreadId);
        Assert.NotSame(_scheduler, TaskScheduler.Current);
        await Task.Factory.StartNew(static () => { });
        await Task.Yield();
        WriteObject(_scheduler.QueuedTaskCount);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncWriteOrdering")]
public sealed class TestAsyncWriteOrderingCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        Task.Run(() => WriteObject("first")).GetAwaiter().GetResult();
        WriteObject("second");
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncInformationTags")]
public sealed class TestAsyncInformationTagsCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        var tags = new[] { "before" };
        Task.Run(() => WriteInformation("message", tags)).GetAwaiter().GetResult();
        tags[0] = "after";

        var informationRecord = new InformationRecord("record-message", "record-source");
        informationRecord.Tags.Add("record-before");
        Task.Run(() => WriteInformation(informationRecord)).GetAwaiter().GetResult();
        informationRecord.Tags[0] = "record-after";
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousError")]
public sealed class TestAsyncSynchronousErrorCommand : AsyncPSCmdlet
{
    private static int _reachedAfterError;

    public static bool ReachedAfterError => Volatile.Read(ref _reachedAfterError) != 0;

    public static void Reset()
        => Volatile.Write(ref _reachedAfterError, 0);

    protected override Task ProcessRecordAsync()
    {
        WriteError(new ErrorRecord(
            new InvalidOperationException("stopping error"),
            "AsyncSynchronousError",
            ErrorCategory.InvalidOperation,
            targetObject: null));
        Volatile.Write(ref _reachedAfterError, 1);
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousEnumeration")]
public sealed class TestAsyncSynchronousEnumerationCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        var values = new[] { 1, 2 };
        WriteObject(values, enumerateCollection: true);
        values[0] = 9;
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousReentrantDrain")]
public sealed class TestAsyncSynchronousReentrantDrainCommand : AsyncPSCmdlet
{
    [Parameter]
    public SwitchParameter Fail { get; set; }

    protected override Task ProcessRecordAsync()
    {
        var streams = CapturePipelineStreams();
        WriteObject(
            new ReentrantWarningEnumerable(streams.WriteWarning),
            enumerateCollection: true);

        if (Fail)
            throw new InvalidOperationException("synchronous reentrant drain failure");

        return Task.CompletedTask;
    }

    private sealed class ReentrantWarningEnumerable(Action<string> writeWarning) : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            writeWarning("reentrant-during-drain");
            yield return "value";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousFailure")]
public sealed class TestAsyncSynchronousFailureCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        Task.Run(() => WriteWarning("before-failure")).GetAwaiter().GetResult();
        throw new InvalidOperationException("synchronous hook failure");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousPipelineStop")]
public sealed class TestAsyncSynchronousPipelineStopCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _cancellationObserved = new();

    public static ManualResetEventSlim CancellationObserved => _cancellationObserved;

    public static void Reset()
    {
        _cancellationObserved.Dispose();
        _cancellationObserved = new ManualResetEventSlim();
    }

    public void InvokeProcessRecord()
        => base.ProcessRecord();

    protected override Task ProcessRecordAsync()
    {
        _ = CancelToken.Register(_cancellationObserved.Set);
        throw new PipelineStoppedException();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncNullInformationTags")]
public sealed class TestAsyncNullInformationTagsCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteInformation("untagged", tags: null);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncStaleInteraction", SupportsShouldProcess = true)]
public sealed class TestAsyncStaleInteractionCommand : AsyncPSCmdlet
{
    private static readonly ManualResetEventSlim SecondRecordStarted = new();
    private static readonly ManualResetEventSlim StaleInteractionFinished = new();
    private static int _recordNumber;

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public string InputObject { get; set; } = string.Empty;

    public static Exception? StaleInteractionException { get; private set; }

    public static void Reset()
    {
        SecondRecordStarted.Reset();
        StaleInteractionFinished.Reset();
        Volatile.Write(ref _recordNumber, 0);
        StaleInteractionException = null;
    }

    protected override async Task ProcessRecordAsync()
    {
        if (Interlocked.Increment(ref _recordNumber) == 1)
        {
            _ = Task.Run(() =>
            {
                Assert.True(
                    SecondRecordStarted.Wait(TimeSpan.FromSeconds(5)),
                    "The second lifecycle did not start in time.");
                try
                {
                    WriteWarning("stale-warning");
                    _ = ShouldProcess("stale-target");
                }
                catch (Exception exception)
                {
                    StaleInteractionException = exception;
                }
                finally
                {
                    StaleInteractionFinished.Set();
                }
            });
            return;
        }

        SecondRecordStarted.Set();
        await Task.Run(() =>
        {
            Assert.True(
                StaleInteractionFinished.Wait(TimeSpan.FromSeconds(5)),
                "The stale interaction did not finish in time.");
        });
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCancellation")]
public sealed class TestAsyncCancellationCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _started = new();

    public static ManualResetEventSlim Started => _started;

    public static void Reset()
    {
        _started.Dispose();
        _started = new ManualResetEventSlim();
    }

    protected override async Task ProcessRecordAsync()
    {
        _started.Set();
        await Task.Delay(Timeout.InfiniteTimeSpan, CancelToken);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCancellationWrite")]
public sealed class TestAsyncCancellationWriteCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _started = new();
    private static ManualResetEventSlim _writeAttempted = new();
    private static TaskCompletionSource<bool> _allowWrite = CreateAllowWriteSource();

    public static ManualResetEventSlim Started => _started;
    public static ManualResetEventSlim WriteAttempted => _writeAttempted;

    public static Exception? BackgroundWriteException { get; private set; }

    public static void AllowWrite()
        => _allowWrite.TrySetResult(true);

    public static void Reset()
    {
        _started.Dispose();
        _writeAttempted.Dispose();
        _started = new ManualResetEventSlim();
        _writeAttempted = new ManualResetEventSlim();
        _allowWrite = CreateAllowWriteSource();
        BackgroundWriteException = null;
    }

    protected override async Task ProcessRecordAsync()
    {
        _started.Set();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancelToken);
        }
        catch (OperationCanceledException) when (CancelToken.IsCancellationRequested)
        {
            await _allowWrite.Task;
            try
            {
                WriteProgress(new ProgressRecord(1, "cancelled", "finishing"));
                WriteWarning("cancelled");
            }
            catch (Exception exception)
            {
                BackgroundWriteException = exception;
            }
            finally
            {
                _writeAttempted.Set();
            }
        }
    }

    private static TaskCompletionSource<bool> CreateAllowWriteSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCapturedCancellationWrite")]
public sealed class TestAsyncCapturedCancellationWriteCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _started = new();
    private static ManualResetEventSlim _writeAttempted = new();

    public static ManualResetEventSlim Started => _started;
    public static ManualResetEventSlim WriteAttempted => _writeAttempted;

    public static Exception? BackgroundWriteException { get; private set; }

    public static void Reset()
    {
        _started.Dispose();
        _writeAttempted.Dispose();
        _started = new ManualResetEventSlim();
        _writeAttempted = new ManualResetEventSlim();
        BackgroundWriteException = null;
    }

    protected override async Task ProcessRecordAsync()
    {
        var streams = CapturePipelineStreams();
        _started.Set();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancelToken);
        }
        catch (OperationCanceledException) when (CancelToken.IsCancellationRequested)
        {
            var completed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ThreadPool.UnsafeQueueUserWorkItem(
                _ =>
                {
                    try
                    {
                        streams.WriteWarning("cancelled callback");
                    }
                    catch (Exception exception)
                    {
                        BackgroundWriteException = exception;
                    }
                    finally
                    {
                        _writeAttempted.Set();
                        completed.TrySetResult(true);
                    }
                },
                null);
            await completed.Task.ConfigureAwait(false);
        }
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncTerminatingError")]
public sealed class TestAsyncTerminatingErrorCommand : AsyncPSCmdlet
{
    private static int _reachedAfterTermination;

    public static bool ReachedAfterTermination => Volatile.Read(ref _reachedAfterTermination) != 0;

    public static void Reset()
        => Volatile.Write(ref _reachedAfterTermination, 0);

    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        ThrowTerminatingError(new ErrorRecord(
            new InvalidOperationException("async terminating failure"),
            "AsyncTerminatingError",
            ErrorCategory.InvalidOperation,
            targetObject: null));
        Volatile.Write(ref _reachedAfterTermination, 1);
        WriteObject("unreachable");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncEarlyTerminatingError")]
public sealed class TestAsyncEarlyTerminatingErrorCommand : AsyncPSCmdlet
{
    protected override void BeginProcessing()
        => ThrowTerminatingError(new ErrorRecord(
            new InvalidOperationException("early terminating failure"),
            "EarlyTerminatingError",
            ErrorCategory.InvalidOperation,
            targetObject: null));
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncShouldContinue")]
public sealed class TestAsyncShouldContinueCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteObject(ShouldContinue("Proceed?", "Question"));
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncClaimedShouldContinue")]
public sealed class TestAsyncClaimedShouldContinueCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _replyObserved = new();
    private static ManualResetEventSlim _cancellationObserved = new();

    public static ManualResetEventSlim ReplyObserved => _replyObserved;
    public static ManualResetEventSlim CancellationObserved => _cancellationObserved;
    public static bool SideEffectStarted { get; private set; }

    public static void Reset()
    {
        _replyObserved.Dispose();
        _cancellationObserved.Dispose();
        _replyObserved = new ManualResetEventSlim();
        _cancellationObserved = new ManualResetEventSlim();
        SideEffectStarted = false;
    }

    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        _ = CancelToken.Register(_cancellationObserved.Set);
        try
        {
            _ = ShouldContinue("Proceed?", "Question");
            SideEffectStarted = true;
        }
        finally
        {
            _replyObserved.Set();
        }
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncOperationCancellation")]
public sealed class TestAsyncOperationCancellationCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        throw new OperationCanceledException("operation timeout");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncLateWrite")]
public sealed class TestAsyncLateWriteCommand : AsyncPSCmdlet
{
    private static TestAsyncLateWriteCommand? _instance;

    public static Exception? LateWriteException { get; private set; }

    public static void Reset()
    {
        _instance = null;
        LateWriteException = null;
    }

    public static void WriteAfterCompletion()
    {
        try
        {
            _instance!.WriteWarning("late");
        }
        catch (Exception exception)
        {
            LateWriteException = exception;
        }
    }

    protected override Task ProcessRecordAsync()
    {
        _instance = this;
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncLateInteraction", SupportsShouldProcess = true)]
public sealed class TestAsyncLateInteractionCommand : AsyncPSCmdlet
{
    private static TestAsyncLateInteractionCommand? _instance;

    public static Exception? LateInteractionException { get; private set; }

    public static void Reset()
    {
        _instance = null;
        LateInteractionException = null;
    }

    public static void InteractAfterCompletion()
        => Task.Run(() =>
        {
            try
            {
                _instance!.ShouldProcess("late-target");
            }
            catch (Exception exception)
            {
                LateInteractionException = exception;
            }
        }).GetAwaiter().GetResult();

    protected override Task ProcessRecordAsync()
    {
        _instance = this;
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDisposable")]
public sealed class TestAsyncDisposableCommand : AsyncPSCmdlet
{
    public CancellationToken StoppingToken => CancelToken;

    public void InvokeStopProcessing()
        => base.StopProcessing();
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncLargeQueuedOutput")]
public sealed class TestAsyncLargeQueuedOutputCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        using var completed = new ManualResetEventSlim();
        Exception? workerException = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 2048; i++)
                    WriteWarning($"queued-{i}");
            }
            catch (Exception exception)
            {
                workerException = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "The producer blocked while filling the pipeline transport.");
        thread.Join();
        if (workerException is not null)
            throw workerException;

        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDirectContext")]
public sealed class TestAsyncDirectContextCommand : AsyncPSCmdlet
{
    public static SynchronizationContext? HostContext { get; private set; }

    protected override void ProcessRecord()
    {
        var previousContext = SynchronizationContext.Current;
        HostContext = new ForwardingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(HostContext);
        try
        {
            base.ProcessRecord();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    protected override Task ProcessRecordAsync()
    {
        WriteObject("context-output");
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "ObserveContext")]
public sealed class TestObserveContextCommand : PSCmdlet
{
    [Parameter(ValueFromPipeline = true)]
    public object? InputObject { get; set; }

    public static SynchronizationContext? ObservedContext { get; private set; }

    protected override void ProcessRecord()
    {
        ObservedContext = SynchronizationContext.Current;
        WriteObject(InputObject);
    }
}
