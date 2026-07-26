using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using PSPublishModule;

namespace PowerForge.Tests;

[Cmdlet(VerbsDiagnostic.Test, "AsyncCapturedCallback")]
public sealed class TestAsyncCapturedCallbackCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        var writeOutput = CapturePipelineWriter();
        var streams = CapturePipelineStreams();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.UnsafeQueueUserWorkItem(
            _ =>
            {
                writeOutput("callback-output");
                streams.WriteWarning("callback-warning");
                completed.TrySetResult(true);
            },
            null);
        await completed.Task.ConfigureAwait(false);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCommandDetail")]
public sealed class TestAsyncCommandDetailCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteCommandDetail("worker-detail");
        WriteObject("completed");
    }
}

[Cmdlet(
    VerbsDiagnostic.Test,
    "AsyncReentrantPump",
    SupportsShouldProcess = true)]
public sealed class TestAsyncReentrantPumpCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteObject(new WarningEnumerable(this), enumerateCollection: true);
        WriteWarning("after-enumeration");
    }

    private sealed class WarningEnumerable : IEnumerable<string>
    {
        private readonly TestAsyncReentrantPumpCommand _command;

        public WarningEnumerable(TestAsyncReentrantPumpCommand command)
        {
            _command = command;
        }

        public IEnumerator<string> GetEnumerator()
        {
            Assert.True(
                _command.ShouldProcess(
                    "enumerated-target"));
            Task.Run(() => _command.WriteWarning("during-enumeration")).GetAwaiter().GetResult();
            yield return "value";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncReentrantFifo")]
public sealed class TestAsyncReentrantFifoCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteObject(new WarningEnumerable(this), enumerateCollection: true);
    }

    private sealed class WarningEnumerable : IEnumerable<string>
    {
        private readonly TestAsyncReentrantFifoCommand _command;

        public WarningEnumerable(TestAsyncReentrantFifoCommand command)
        {
            _command = command;
        }

        public IEnumerator<string> GetEnumerator()
        {
            Task.Run(() => _command.WriteWarning("queued-first")).GetAwaiter().GetResult();
            _command.WriteWarning("direct-second");
            yield return "value";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCapturedEnumeration")]
public sealed class TestAsyncCapturedEnumerationCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        var streams = CapturePipelineStreams();
        WriteObject(
            new CapturedWarningEnumerable(
                message => streams.WriteWarning(message)),
            enumerateCollection: true);
    }

    private sealed class CapturedWarningEnumerable : IEnumerable<string>
    {
        private readonly Action<string> _writeWarning;

        public CapturedWarningEnumerable(Action<string> writeWarning)
        {
            _writeWarning = writeWarning;
        }

        public IEnumerator<string> GetEnumerator()
        {
            using var completed = new ManualResetEventSlim();
            Exception? callbackException = null;
            ThreadPool.UnsafeQueueUserWorkItem(
                _ =>
                {
                    try
                    {
                        _writeWarning("captured-during-enumeration");
                    }
                    catch (Exception exception)
                    {
                        callbackException = exception;
                    }
                    finally
                    {
                        completed.Set();
                    }
                },
                null);
            Assert.True(
                completed.Wait(TimeSpan.FromSeconds(5)),
                "The context-free callback did not complete in time.");
            if (callbackException is not null)
                throw callbackException;

            yield return "value";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDerivedEndProcessing")]
public sealed class TestAsyncDerivedEndProcessingCommand : AsyncPSCmdlet
{
    protected override void EndProcessing()
    {
        WriteObject("before-base-end");
        base.EndProcessing();
        WriteObject("after-base-end");
        WriteWarning("after-base-warning");
    }
}

[Cmdlet(
    VerbsDiagnostic.Test,
    "AsyncDirectBarrierTail",
    SupportsShouldProcess = true)]
public sealed class TestAsyncDirectBarrierTailCommand : AsyncPSCmdlet
{
    private static int _tailEnumerated;

    public static bool TailEnumeratedBeforeInteraction { get; private set; }

    public static void Reset()
    {
        Volatile.Write(ref _tailEnumerated, 0);
        TailEnumeratedBeforeInteraction = false;
    }

    protected override Task ProcessRecordAsync()
    {
        var streams = CapturePipelineStreams();
        Task.Run(
                () => streams.WriteObject(
                    new OuterEnumerable(streams),
                    enumerateCollection: true))
            .GetAwaiter()
            .GetResult();

        _ = ShouldProcess("barrier-target");
        TailEnumeratedBeforeInteraction = Volatile.Read(ref _tailEnumerated) != 0;
        return Task.CompletedTask;
    }

    private sealed class OuterEnumerable(CapturedPipelineStreams streams) : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            streams.WriteObject(new TailEnumerable(), enumerateCollection: true);
            yield return "outer";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private sealed class TailEnumerable : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            Volatile.Write(ref _tailEnumerated, 1);
            yield return "tail";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(
    VerbsDiagnostic.Test,
    "AsyncReentrantInteractionTail",
    SupportsShouldProcess = true)]
public sealed class TestAsyncReentrantInteractionTailCommand : AsyncPSCmdlet
{
    private static int _tailEnumerated;

    public static bool TailEnumeratedBeforeInteraction { get; private set; }

    public static void Reset()
    {
        Volatile.Write(ref _tailEnumerated, 0);
        TailEnumeratedBeforeInteraction = false;
    }

    protected override Task ProcessRecordAsync()
    {
        var streams =
            CapturePipelineStreams();
        Task.Run(
                () => streams.WriteObject(
                    new OuterEnumerable(this),
                    enumerateCollection: true))
            .GetAwaiter()
            .GetResult();
        return Task.CompletedTask;
    }

    private sealed class OuterEnumerable(
        TestAsyncReentrantInteractionTailCommand command)
        : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            command.WriteObject(
                new TailEnumerable(),
                enumerateCollection: true);
            _ = command.ShouldProcess(
                "reentrant-target");
            TailEnumeratedBeforeInteraction =
                Volatile.Read(ref _tailEnumerated) != 0;
            yield return "outer";
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private sealed class TailEnumerable : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            Volatile.Write(ref _tailEnumerated, 1);
            yield return "tail";
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(
    VerbsDiagnostic.Test,
    "AsyncFailureRecoveryWrite")]
public sealed class TestAsyncFailureRecoveryWriteCommand : AsyncPSCmdlet
{
    public static bool TransportClearedAfterFailure { get; private set; }

    public static void Reset()
        => TransportClearedAfterFailure = false;

    protected override void ProcessRecord()
    {
        try
        {
            base.ProcessRecord();
        }
        catch (Exception)
        {
            TransportClearedAfterFailure =
                typeof(AsyncPSCmdlet)
                    .GetField(
                        "_currentOutPipe",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)!
                    .GetValue(this) is null;
        }
    }

    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        WriteObject(
            new FailingEnumerable(),
            enumerateCollection: true);
    }

    private sealed class FailingEnumerable : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            throw new InvalidOperationException(
                "enumeration failed");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(
    VerbsDiagnostic.Test,
    "AsyncHookContextIdentity")]
public sealed class TestAsyncHookContextIdentityCommand : AsyncPSCmdlet
{
    private SynchronizationContext? _beginContext;

    protected override Task BeginProcessingAsync()
    {
        _beginContext =
            SynchronizationContext.Current;
        return Task.CompletedTask;
    }

    protected override Task ProcessRecordAsync()
    {
        WriteObject(
            ReferenceEquals(
                _beginContext,
                SynchronizationContext.Current));
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncEarlyShouldProcess", SupportsShouldProcess = true)]
public sealed class TestAsyncEarlyShouldProcessCommand : AsyncPSCmdlet
{
    private bool _approved;

    protected override void ProcessRecord()
    {
        WriteObject("early-output");
        _approved = ShouldProcess("target");
        base.ProcessRecord();
    }

    protected override Task ProcessRecordAsync()
    {
        WriteObject(_approved);
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncConstructorWrite")]
public sealed class TestAsyncConstructorWriteCommand : AsyncPSCmdlet
{
    public TestAsyncConstructorWriteCommand()
        => WriteWarning("constructor output must not reach PowerShell");

    protected override Task ProcessRecordAsync()
    {
        WriteObject("completed");
        return Task.CompletedTask;
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncProgressSnapshot")]
public sealed class TestAsyncProgressSnapshotCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        var progress = new ProgressRecord(17, "snapshot", "queued")
        {
            PercentComplete = 10
        };
        var totalProperty = progress.GetType().GetProperty("Total");
        if (totalProperty is not null)
        {
            totalProperty.SetValue(
                progress,
                Convert.ChangeType(42, totalProperty.PropertyType, CultureInfo.InvariantCulture));
        }
        WriteProgress(progress);
        progress.PercentComplete = 90;
        if (totalProperty is not null)
        {
            totalProperty.SetValue(
                progress,
                Convert.ChangeType(99, totalProperty.PropertyType, CultureInfo.InvariantCulture));
        }
        WriteObject("completed");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncCapturedErrorSnapshot")]
public sealed class TestAsyncCapturedErrorSnapshotCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        var streams = CapturePipelineStreams();
        WriteObject(new ErrorSnapshotEnumerable(streams), enumerateCollection: true);
    }

    private sealed class ErrorSnapshotEnumerable(CapturedPipelineStreams streams) : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            var error = new ErrorRecord(
                new InvalidOperationException("snapshot failure"),
                "CapturedErrorSnapshot",
                ErrorCategory.InvalidOperation,
                targetObject: null)
            {
                ErrorDetails = new ErrorDetails("original details")
            };
            streams.WriteError(error);
            error.ErrorDetails = new ErrorDetails("mutated details");
            yield return "completed";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncTerminatingErrorSnapshot")]
public sealed class TestAsyncTerminatingErrorSnapshotCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        var error = new ErrorRecord(
            new InvalidOperationException("terminating snapshot failure"),
            "TerminatingErrorSnapshot",
            ErrorCategory.InvalidOperation,
            targetObject: null)
        {
            ErrorDetails = new ErrorDetails("original terminating details")
        };
        try
        {
            ThrowTerminatingError(error);
        }
        catch (PipelineStoppedException)
        {
            error.ErrorDetails = new ErrorDetails("mutated terminating details");
            throw;
        }
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncStaleProgress")]
public sealed class TestAsyncStaleProgressCommand : AsyncPSCmdlet
{
    private IProgress<int>? _firstRecordProgress;
    private int _record;

    [Parameter(ValueFromPipeline = true)]
    public int InputObject { get; set; }

    protected override async Task ProcessRecordAsync()
    {
        _record++;
        if (_record == 1)
        {
            _firstRecordProgress = new Progress<int>(
                _ => WriteWarning("stale-first-record-warning"));
            return;
        }

        using var completed = new ManualResetEventSlim();
        Exception? callbackException = null;
        ThreadPool.UnsafeQueueUserWorkItem(
            _ =>
            {
                try
                {
                    _firstRecordProgress!.Report(InputObject);
                }
                catch (Exception exception)
                {
                    callbackException = exception;
                }
                finally
                {
                    completed.Set();
                }
            },
            null);
        Assert.True(
            completed.Wait(TimeSpan.FromSeconds(5)),
            "The context-free progress producer did not complete in time.");
        await Task.Delay(100);
        if (callbackException is not null)
            throw callbackException;
        WriteObject(InputObject);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncEnumeratorFailure")]
public sealed class TestAsyncEnumeratorFailureCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        await Task.Yield();
        var streams = CapturePipelineStreams();
        WriteObject(new FailingEnumerable(streams), enumerateCollection: true);
    }

    private sealed class FailingEnumerable(CapturedPipelineStreams streams) : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            streams.WriteWarning("before enumeration failure");
            yield return "first";
            throw new InvalidOperationException("enumeration failed");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncPipelineFailureWithThrowingCancellation")]
public sealed class TestAsyncPipelineFailureWithThrowingCancellationCommand : AsyncPSCmdlet
{
    protected override async Task ProcessRecordAsync()
    {
        _ = CancelToken.Register(
            static () => throw new InvalidOperationException("cancellation callback failed"));
        await Task.Yield();
        ThrowTerminatingError(new ErrorRecord(
            new InvalidOperationException("pipeline failure"),
            "PipelineFailure",
            ErrorCategory.InvalidOperation,
            targetObject: null));
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDisposeDuringHook")]
public sealed class TestAsyncDisposeDuringHookCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _tokenRead = new();

    public static ManualResetEventSlim TokenRead => _tokenRead;

    public static bool ReadTokenAfterDispose { get; private set; }

    public static Exception? TokenReadException { get; private set; }

    public static void Reset()
    {
        _tokenRead.Dispose();
        _tokenRead = new ManualResetEventSlim();
        ReadTokenAfterDispose = false;
        TokenReadException = null;
    }

    protected override async Task ProcessRecordAsync()
    {
        Dispose();
        await Task.Yield();
        try
        {
            var token = CancelToken;
            ReadTokenAfterDispose = true;
            _tokenRead.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TokenReadException = exception;
            _tokenRead.Set();
            throw;
        }
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDisposeDuringCancellation")]
public sealed class TestAsyncDisposeDuringCancellationCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _callbackCompleted = new();

    public static ManualResetEventSlim CallbackCompleted => _callbackCompleted;

    public static bool TokenReadDuringCancellation { get; private set; }

    public static Exception? TokenReadException { get; private set; }

    public static void Reset()
    {
        _callbackCompleted.Dispose();
        _callbackCompleted = new ManualResetEventSlim();
        TokenReadDuringCancellation = false;
        TokenReadException = null;
    }

    protected override Task ProcessRecordAsync()
    {
        using var cancellationEntered = new ManualResetEventSlim();
        using var releaseCancellation = new ManualResetEventSlim();
        _ = CancelToken.Register(
            () =>
            {
                cancellationEntered.Set();
                releaseCancellation.Wait(TimeSpan.FromSeconds(5));
                try
                {
                    _ = CancelToken;
                    TokenReadDuringCancellation = true;
                }
                catch (Exception exception)
                {
                    TokenReadException = exception;
                }
                finally
                {
                    _callbackCompleted.Set();
                }
            });

        _ = Task.Run(Dispose);
        Assert.True(
            cancellationEntered.Wait(TimeSpan.FromSeconds(5)),
            "Cancellation did not enter the registered callback.");
        ThreadPool.QueueUserWorkItem(
            _ =>
            {
                Thread.Sleep(100);
                releaseCancellation.Set();
            });
        return Task.CompletedTask;
    }

    protected override void EndProcessing()
    {
        // ProcessRecord intentionally disposes the command to exercise the cancellation race.
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncDirectPipelineStop")]
public sealed class TestAsyncDirectPipelineStopCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _cancellationObserved = new();

    public static ManualResetEventSlim CancellationObserved => _cancellationObserved;

    public static void Reset()
    {
        _cancellationObserved.Dispose();
        _cancellationObserved = new ManualResetEventSlim();
    }

    protected override async Task ProcessRecordAsync()
    {
        _ = CancelToken.Register(_cancellationObserved.Set);
        await Task.CompletedTask;
        WriteObject(1);
        WriteObject(2);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSuccessfulCompletion")]
public sealed class TestAsyncSuccessfulCompletionCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _cancellationObserved = new();

    public static ManualResetEventSlim CancellationObserved => _cancellationObserved;

    public static void Reset()
    {
        _cancellationObserved.Dispose();
        _cancellationObserved = new ManualResetEventSlim();
    }

    protected override async Task ProcessRecordAsync()
    {
        _ = CancelToken.Register(_cancellationObserved.Set);
        await Task.Yield();
        WriteObject("completed");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncLateProgress")]
public sealed class TestAsyncLateProgressCommand : AsyncPSCmdlet
{
    private static ManualResetEventSlim _callbackCompleted = new();
    private static ManualResetEventSlim _initialized = new();
    private static TestAsyncLateProgressCommand? _instance;
    private static IProgress<int>? _progress;

    public static ManualResetEventSlim CallbackCompleted => _callbackCompleted;

    public static ManualResetEventSlim Initialized => _initialized;

    public static Exception? LateWriteException { get; private set; }

    public static void Reset()
    {
        _callbackCompleted.Dispose();
        _initialized.Dispose();
        _callbackCompleted = new ManualResetEventSlim();
        _initialized = new ManualResetEventSlim();
        _instance = null;
        _progress = null;
        LateWriteException = null;
    }

    public static void ReportAfterStop()
        => _progress!.Report(50);

    public static void WriteAfterStop()
    {
        try
        {
            _instance!.WriteWarning("after stop");
        }
        catch (Exception exception)
        {
            LateWriteException = exception;
        }
    }

    protected override async Task ProcessRecordAsync()
    {
        _instance = this;
        _progress = new Progress<int>(percent =>
        {
            try
            {
                WriteProgress(new ProgressRecord(1, "late", "after stop")
                {
                    PercentComplete = percent
                });
            }
            finally
            {
                _callbackCompleted.Set();
            }
        });
        _initialized.Set();
        await Task.Delay(Timeout.InfiniteTimeSpan, CancelToken);
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousStop")]
public sealed class TestAsyncSynchronousStopCommand : AsyncPSCmdlet
{
    public void InvokeProcessRecord()
        => base.ProcessRecord();

    protected override Task ProcessRecordAsync()
    {
        Dispose();
        throw new OperationCanceledException("stopped synchronously");
    }
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncStoppedBeforeHook")]
public sealed class TestAsyncStoppedBeforeHookCommand : AsyncPSCmdlet
{
    public bool HookInvoked { get; private set; }

    public void InvokeProcessRecord()
        => base.ProcessRecord();

    public void InvokeStopProcessing()
        => base.StopProcessing();

    protected override Task ProcessRecordAsync()
    {
        HookInvoked = true;
        return Task.CompletedTask;
    }
}

public sealed class ChoiceHost(
    bool approved,
    Exception? promptFailure = null,
    ManualResetEventSlim? promptEntered = null,
    ManualResetEventSlim? promptRelease = null) : PSHost
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly ChoiceHostUserInterface _ui =
        new(approved, promptFailure, promptEntered, promptRelease);

    public override Guid InstanceId => _id;
    public override string Name => nameof(ChoiceHost);
    public override Version Version => new(1, 0);
    public override PSHostUserInterface UI => _ui;

    public IReadOnlyList<ProgressRecord> ProgressRecords => _ui.ProgressRecords;
    public override CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
    public override CultureInfo CurrentUICulture => CultureInfo.InvariantCulture;
    public override void EnterNestedPrompt() { }
    public override void ExitNestedPrompt() { }
    public override void NotifyBeginApplication() { }
    public override void NotifyEndApplication() { }
    public override void SetShouldExit(int exitCode) { }
}

public sealed class ChoiceHostUserInterface(
    bool approved,
    Exception? promptFailure,
    ManualResetEventSlim? promptEntered,
    ManualResetEventSlim? promptRelease) : PSHostUserInterface
{
    private readonly List<ProgressRecord> _progressRecords = new();

    public IReadOnlyList<ProgressRecord> ProgressRecords => _progressRecords;

    public override PSHostRawUserInterface RawUI => null!;

    public override int PromptForChoice(
        string caption,
        string message,
        Collection<ChoiceDescription> choices,
        int defaultChoice)
    {
        promptEntered?.Set();
        Assert.True(
            promptRelease?.Wait(TimeSpan.FromSeconds(5)) ?? true,
            "The test host prompt was not released in time.");
        return promptFailure is null ? (approved ? 0 : 1) : throw promptFailure;
    }

    public override string ReadLine() => string.Empty;
    public override SecureString ReadLineAsSecureString() => new();
    public override void Write(string value) { }
    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) { }
    public override void WriteLine(string value) { }
    public override void WriteErrorLine(string value) { }
    public override void WriteDebugLine(string message) { }
    public override void WriteProgress(long sourceId, ProgressRecord record)
        => _progressRecords.Add(record);
    public override void WriteVerboseLine(string message) { }
    public override void WriteWarningLine(string message) { }

    public override PSCredential PromptForCredential(
        string caption,
        string message,
        string userName,
        string targetName)
        => new(userName, new SecureString());

    public override PSCredential PromptForCredential(
        string caption,
        string message,
        string userName,
        string targetName,
        PSCredentialTypes allowedCredentialTypes,
        PSCredentialUIOptions options)
        => PromptForCredential(caption, message, userName, targetName);

    public override Dictionary<string, PSObject> Prompt(
        string caption,
        string message,
        Collection<FieldDescription> descriptions)
        => [];
}

public sealed class ForwardingTaskScheduler : TaskScheduler
{
    private int _executeNextTaskInline;
    private int _queuedTaskCount;

    public int QueuedTaskCount => Volatile.Read(ref _queuedTaskCount);

    public void Run(Action action)
    {
        Volatile.Write(ref _executeNextTaskInline, 1);
        var task = Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.None,
            this);
        task.GetAwaiter().GetResult();
    }

    protected override IEnumerable<Task> GetScheduledTasks()
        => Array.Empty<Task>();

    protected override void QueueTask(Task task)
    {
        if (Interlocked.Exchange(ref _executeNextTaskInline, 0) == 1)
        {
            Assert.True(TryExecuteTask(task));
            return;
        }

        Interlocked.Increment(ref _queuedTaskCount);
        ThreadPool.QueueUserWorkItem(_ => TryExecuteTask(task));
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        => false;
}

public sealed class ForwardingSynchronizationContext : SynchronizationContext
{
    private int _postCount;

    public int PostCount => Volatile.Read(ref _postCount);

    public override void Post(SendOrPostCallback callback, object? state)
    {
        Interlocked.Increment(ref _postCount);
        ThreadPool.QueueUserWorkItem(_ => callback(state));
    }
}
