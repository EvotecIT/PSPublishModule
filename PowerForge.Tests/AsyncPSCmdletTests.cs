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

public sealed class AsyncPSCmdletTests
{
    [Fact]
    public void AsyncPSCmdlet_starts_hooks_on_the_pipeline_thread_and_pumps_after_await()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncThreadAffinity",
            typeof(TestAsyncThreadAffinityCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncThreadAffinity");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var item = Assert.Single(result);
        Assert.Equal("post-await-output", item.BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_drains_worker_thread_writes_when_task_completes_synchronously()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncQueuedOutput",
            typeof(TestAsyncQueuedOutputCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncQueuedOutput");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var item = Assert.Single(result);
        Assert.Equal("queued-output", item.BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_capture_a_host_synchronization_context()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncSynchronizationContext",
            typeof(TestAsyncSynchronizationContextCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncSynchronizationContext");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var item = Assert.Single(result);
        Assert.Equal(0, item.BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_capture_a_custom_task_scheduler()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncTaskScheduler",
            typeof(TestAsyncTaskSchedulerCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncTaskScheduler");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var item = Assert.Single(result);
        Assert.Equal(0, item.BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_translates_cancellation_to_a_pipeline_stop()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCancellation",
            typeof(TestAsyncCancellationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCancellation");
        TestAsyncCancellationCommand.Reset();

        var invocation = powerShell.BeginInvoke();
        Assert.True(
            TestAsyncCancellationCommand.Started.Wait(TimeSpan.FromSeconds(5)),
            "The asynchronous cmdlet did not start in time.");

        powerShell.Stop();

        var exception = Assert.Throws<PipelineStoppedException>(() => powerShell.EndInvoke(invocation));
        Assert.IsNotType<OperationCanceledException>(exception);
    }

    [Fact]
    public void AsyncPSCmdlet_marshals_terminating_errors_to_the_pipeline_thread()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncTerminatingError",
            typeof(TestAsyncTerminatingErrorCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncTerminatingError");
        TestAsyncTerminatingErrorCommand.Reset();

        var exception = Assert.Throws<CmdletInvocationException>(() => powerShell.Invoke());

        Assert.StartsWith("AsyncTerminatingError,", exception.ErrorRecord.FullyQualifiedErrorId, StringComparison.Ordinal);
        Assert.Equal("async terminating failure", exception.InnerException?.Message);
        Assert.False(TestAsyncTerminatingErrorCommand.ReachedAfterTermination);
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_terminating_errors_before_the_initial_base_hook()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncEarlyTerminatingError",
            typeof(TestAsyncEarlyTerminatingErrorCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncEarlyTerminatingError");

        var exception = Assert.Throws<CmdletInvocationException>(() => powerShell.Invoke());

        Assert.StartsWith("EarlyTerminatingError,", exception.ErrorRecord.FullyQualifiedErrorId, StringComparison.Ordinal);
        Assert.Equal("early terminating failure", exception.InnerException?.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AsyncPSCmdlet_marshals_ShouldContinue_to_the_pipeline_thread(bool approved)
    {
        var sessionState = InitialSessionState.Create();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncShouldContinue",
            typeof(TestAsyncShouldContinueCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(new ChoiceHost(approved), sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncShouldContinue");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var item = Assert.Single(result);
        Assert.Equal(approved, item.BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_operation_cancellation_that_is_not_a_pipeline_stop()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncOperationCancellation",
            typeof(TestAsyncOperationCancellationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncOperationCancellation");

        var exception = Assert.Throws<CmdletInvocationException>(() => powerShell.Invoke());

        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.IsNotType<PipelineStoppedException>(exception.InnerException);
    }

    [Fact]
    public void AsyncPSCmdlet_drops_late_worker_writes_after_the_pipeline_closes()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncLateWrite",
            typeof(TestAsyncLateWriteCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncLateWrite");
        TestAsyncLateWriteCommand.Reset();

        powerShell.Invoke();
        TestAsyncLateWriteCommand.WriteAfterCompletion();

        Assert.Null(TestAsyncLateWriteCommand.LateWriteException);
    }

    [Fact]
    public void AsyncPSCmdlet_rejects_late_worker_interactions_after_the_pipeline_closes()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncLateInteraction",
            typeof(TestAsyncLateInteractionCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncLateInteraction");
        TestAsyncLateInteractionCommand.Reset();

        powerShell.Invoke();
        TestAsyncLateInteractionCommand.InteractAfterCompletion();

        Assert.True(
            TestAsyncLateInteractionCommand.LateInteractionException is
                InvalidOperationException or PipelineStoppedException,
            $"Unexpected late-interaction result: {TestAsyncLateInteractionCommand.LateInteractionException}");
    }

    [Fact]
    public void AsyncPSCmdlet_dispose_requests_cancellation()
    {
        using var command = new TestAsyncDisposableCommand();
        var stoppingToken = command.StoppingToken;

        command.Dispose();

        Assert.True(stoppingToken.IsCancellationRequested);
    }

    [Fact]
    public void AsyncPSCmdlet_allows_ShouldProcess_before_the_async_base_hook_starts()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncEarlyShouldProcess",
            typeof(TestAsyncEarlyShouldProcessCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncEarlyShouldProcess")
            .AddParameter("Confirm", false);

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        Assert.Collection(
            result,
            item => Assert.Equal("early-output", item.BaseObject),
            item => Assert.True((bool)item.BaseObject));
    }

    [Fact]
    public void AsyncPSCmdlet_keeps_the_cancellation_source_alive_until_the_async_hook_exits()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDisposeDuringHook",
            typeof(TestAsyncDisposeDuringHookCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncDisposeDuringHook");
        TestAsyncDisposeDuringHookCommand.Reset();

        powerShell.Invoke();

        Assert.True(TestAsyncDisposeDuringHookCommand.ReadTokenAfterDispose);
        Assert.Null(TestAsyncDisposeDuringHookCommand.TokenReadException);
    }

    [Fact]
    public void AsyncPSCmdlet_normalizes_synchronous_cancellation_after_stop()
    {
        using var command = new TestAsyncSynchronousStopCommand();

        Assert.Throws<PipelineStoppedException>(command.InvokeProcessRecord);
    }
}

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

    protected override void ProcessRecord()
        => _scheduler.Run(base.ProcessRecord);

    protected override async Task ProcessRecordAsync()
    {
        Assert.Same(TaskScheduler.Default, TaskScheduler.Current);
        await Task.Factory.StartNew(static () => { });
        await Task.Yield();
        WriteObject(_scheduler.QueuedTaskCount);
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
    public static bool ReadTokenAfterDispose { get; private set; }

    public static Exception? TokenReadException { get; private set; }

    public static void Reset()
    {
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
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TokenReadException = exception;
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

public sealed class ChoiceHost(bool approved) : PSHost
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly ChoiceHostUserInterface _ui = new(approved);

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

public sealed class ChoiceHostUserInterface(bool approved) : PSHostUserInterface
{
    public override PSHostRawUserInterface RawUI => null!;

    public override int PromptForChoice(
        string caption,
        string message,
        Collection<ChoiceDescription> choices,
        int defaultChoice)
        => approved ? 0 : 1;

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
