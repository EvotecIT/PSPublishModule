using System.Management.Automation;
using System.Management.Automation.Runspaces;
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
