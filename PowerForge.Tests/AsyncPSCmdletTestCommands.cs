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

[Cmdlet(VerbsDiagnostic.Test, "AsyncSynchronousFailure")]
public sealed class TestAsyncSynchronousFailureCommand : AsyncPSCmdlet
{
    protected override Task ProcessRecordAsync()
    {
        Task.Run(() => WriteWarning("before-failure")).GetAwaiter().GetResult();
        throw new InvalidOperationException("synchronous hook failure");
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

    public static ManualResetEventSlim Started => _started;

    public static Exception? BackgroundWriteException { get; private set; }

    public static void Reset()
    {
        _started.Dispose();
        _started = new ManualResetEventSlim();
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
            try
            {
                WriteProgress(new ProgressRecord(1, "cancelled", "finishing"));
                WriteWarning("cancelled");
            }
            catch (Exception exception)
            {
                BackgroundWriteException = exception;
            }
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
}

[Cmdlet(VerbsDiagnostic.Test, "AsyncReentrantPump")]
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
            _command.WriteWarning("during-enumeration");
            yield return "value";
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
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

public sealed class ChoiceHost(bool approved, Exception? promptFailure = null) : PSHost
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly ChoiceHostUserInterface _ui = new(approved, promptFailure);

    public override Guid InstanceId => _id;
    public override string Name => nameof(ChoiceHost);
    public override Version Version => new(1, 0);
    public override PSHostUserInterface UI => _ui;
    public override CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
    public override CultureInfo CurrentUICulture => CultureInfo.InvariantCulture;
    public override void EnterNestedPrompt() { }
    public override void ExitNestedPrompt() { }
    public override void NotifyBeginApplication() { }
    public override void NotifyEndApplication() { }
    public override void SetShouldExit(int exitCode) { }
}

public sealed class ChoiceHostUserInterface(bool approved, Exception? promptFailure) : PSHostUserInterface
{
    public override PSHostRawUserInterface RawUI => null!;

    public override int PromptForChoice(
        string caption,
        string message,
        Collection<ChoiceDescription> choices,
        int defaultChoice)
        => promptFailure is null ? (approved ? 0 : 1) : throw promptFailure;

    public override string ReadLine() => string.Empty;
    public override SecureString ReadLineAsSecureString() => new();
    public override void Write(string value) { }
    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) { }
    public override void WriteLine(string value) { }
    public override void WriteErrorLine(string value) { }
    public override void WriteDebugLine(string message) { }
    public override void WriteProgress(long sourceId, ProgressRecord record) { }
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
