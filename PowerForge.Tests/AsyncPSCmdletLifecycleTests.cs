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

public sealed partial class AsyncPSCmdletTests
{
[Fact]
    public void AsyncPSCmdlet_dispose_without_an_active_hook_does_not_signal_stop()
    {
        using var command = new TestAsyncDisposableCommand();
        var stoppingToken = command.StoppingToken;

        command.Dispose();

        Assert.False(stoppingToken.IsCancellationRequested);
    }

    [Fact]
    public void AsyncPSCmdlet_drops_pre_lifecycle_writes_from_other_threads()
    {
        using var command = new TestAsyncDisposableCommand();
        Exception? exception = null;
        var workerThreadId = 0;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            workerThreadId = Environment.CurrentManagedThreadId;
            exception = Record.Exception(() => command.WriteWarning("too-early"));
            completed.Set();
        });

        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        thread.Join();
        Assert.NotEqual(Environment.CurrentManagedThreadId, workerThreadId);
        Assert.Null(exception);
    }

    [Fact]
    public void AsyncPSCmdlet_finishes_disposal_when_cancellation_callbacks_throw()
    {
        var command = new TestAsyncDisposableCommand();
        var stoppingToken = command.StoppingToken;
        using var registration = stoppingToken.Register(
            static () => throw new InvalidOperationException("cancellation callback failed"));

        command.InvokeStopProcessing();
        command.Dispose();

        Assert.True(stoppingToken.IsCancellationRequested);
        var sourceField = typeof(AsyncPSCmdlet).GetField(
            "_cancelSource",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var source = Assert.IsType<CancellationTokenSource>(sourceField!.GetValue(command));
        Assert.Throws<ObjectDisposedException>(source.Cancel);
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_deadlock_when_a_synchronous_hook_queues_more_than_the_transport_capacity()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncLargeQueuedOutput",
            typeof(TestAsyncLargeQueuedOutputCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncLargeQueuedOutput");

        powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Equal(2048, powerShell.Streams.Warning.Count);
    }

    [Fact]
    public void AsyncPSCmdlet_restores_the_host_context_around_direct_pipeline_writes()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDirectContext",
            typeof(TestAsyncDirectContextCommand),
            helpFileName: null));
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-ObserveContext",
            typeof(TestObserveContextCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncDirectContext")
            .AddCommand("Test-ObserveContext");

        var result = powerShell.Invoke();

        Assert.Equal("context-output", Assert.Single(result).BaseObject);
        Assert.Same(TestAsyncDirectContextCommand.HostContext, TestObserveContextCommand.ObservedContext);
    }

    [Fact]
    public void AsyncPSCmdlet_accepts_context_free_callbacks_through_a_captured_writer()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCapturedCallback",
            typeof(TestAsyncCapturedCallbackCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCapturedCallback");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Equal("callback-output", Assert.Single(result).BaseObject);
        Assert.Equal(
            "callback-warning",
            Assert.Single(powerShell.Streams.Warning).Message);
    }

    [Fact]
    public void AsyncPSCmdlet_marshals_command_details_to_the_pipeline_thread()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCommandDetail",
            typeof(TestAsyncCommandDetailCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCommandDetail");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Equal("completed", Assert.Single(result).BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_recursively_drain_when_enumeration_writes_to_the_pipeline()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncReentrantPump",
            typeof(TestAsyncReentrantPumpCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncReentrantPump")
            .AddParameter("Confirm", false);

        var result = powerShell.Invoke();

        Assert.Collection(result, item => Assert.Equal("value", item.BaseObject));
        Assert.Equal(
            ["after-enumeration", "during-enumeration"],
            powerShell.Streams.Warning.Select(static warning => warning.Message).Order());
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_fifo_order_during_reentrant_queue_drains()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncReentrantFifo",
            typeof(TestAsyncReentrantFifoCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncReentrantFifo");

        var result = powerShell.Invoke();

        Assert.Equal("value", Assert.Single(result).BaseObject);
        Assert.Equal(
            ["queued-first", "direct-second"],
            powerShell.Streams.Warning.Select(static warning => warning.Message));
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_context_free_callbacks_while_their_output_is_pumped()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCapturedEnumeration",
            typeof(TestAsyncCapturedEnumerationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCapturedEnumeration");

        var result = powerShell.Invoke();

        Assert.Equal("value", Assert.Single(result).BaseObject);
        Assert.Equal(
            "captured-during-enumeration",
            Assert.Single(
                powerShell.Streams.Warning).Message);
    }

    [Fact]
    public void AsyncPSCmdlet_keeps_derived_EndProcessing_active_after_the_base_call()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDerivedEndProcessing",
            typeof(TestAsyncDerivedEndProcessingCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncDerivedEndProcessing");

        var result = powerShell.Invoke();

        Assert.Collection(
            result,
            item => Assert.Equal("before-base-end", item.BaseObject),
            item => Assert.Equal("after-base-end", item.BaseObject));
        Assert.Equal(
            "after-base-warning",
            Assert.Single(powerShell.Streams.Warning).Message);
    }

    [Fact]
    public void AsyncPSCmdlet_drains_causal_tail_records_before_direct_interactions()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDirectBarrierTail",
            typeof(TestAsyncDirectBarrierTailCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncDirectBarrierTail")
            .AddParameter("Confirm", false);
        TestAsyncDirectBarrierTailCommand.Reset();

        var result = powerShell.Invoke();

        Assert.Equal(
            ["outer", "tail"],
            result.Select(static item => item.BaseObject));
        Assert.True(TestAsyncDirectBarrierTailCommand.TailEnumeratedBeforeInteraction);
    }

    [Fact]
    public void AsyncPSCmdlet_drains_reentrant_records_before_direct_interactions()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncReentrantInteractionTail",
            typeof(
                TestAsyncReentrantInteractionTailCommand),
            helpFileName: null));

        using var runspace =
            RunspaceFactory.CreateRunspace(
                sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand(
                "Test-AsyncReentrantInteractionTail")
            .AddParameter(
                "Confirm",
                false);
        TestAsyncReentrantInteractionTailCommand
            .Reset();

        var result = powerShell.Invoke();

        Assert.Equal(
            ["tail", "outer"],
            result.Select(
                static item => item.BaseObject));
        Assert.True(
            TestAsyncReentrantInteractionTailCommand
                .TailEnumeratedBeforeInteraction);
    }

    [Fact]
    public void AsyncPSCmdlet_clears_a_completed_pipe_before_a_recovery_write()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncFailureRecoveryWrite",
            typeof(
                TestAsyncFailureRecoveryWriteCommand),
            helpFileName: null));

        using var runspace =
            RunspaceFactory.CreateRunspace(
                sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand(
            "Test-AsyncFailureRecoveryWrite");
        TestAsyncFailureRecoveryWriteCommand
            .Reset();

        var result = powerShell.Invoke();

        Assert.Empty(result);
        Assert.True(
            TestAsyncFailureRecoveryWriteCommand
                .TransportClearedAfterFailure);
    }

    [Fact]
    public void AsyncPSCmdlet_uses_a_distinct_context_for_each_hook()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncHookContextIdentity",
            typeof(
                TestAsyncHookContextIdentityCommand),
            helpFileName: null));

        using var runspace =
            RunspaceFactory.CreateRunspace(
                sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand(
            "Test-AsyncHookContextIdentity");

        var result = powerShell.Invoke();

        Assert.False(
            (bool)Assert.Single(
                result).BaseObject);
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
    public void AsyncPSCmdlet_drops_constructor_writes_before_pipeline_startup()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncConstructorWrite",
            typeof(TestAsyncConstructorWriteCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncConstructorWrite");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Empty(powerShell.Streams.Warning);
        Assert.Equal("completed", Assert.Single(result).BaseObject);
    }

    [Fact]
    public void AsyncPSCmdlet_snapshots_mutable_progress_before_queueing()
    {
        var sessionState = InitialSessionState.Create();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncProgressSnapshot",
            typeof(TestAsyncProgressSnapshotCommand),
            helpFileName: null));

        var host = new ChoiceHost(approved: true);
        using var runspace = RunspaceFactory.CreateRunspace(host, sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncProgressSnapshot");

        var result = powerShell.Invoke();

        Assert.Equal("completed", Assert.Single(result).BaseObject);
        var captured = Assert.Single(host.ProgressRecords);
        Assert.Equal(10, captured.PercentComplete);
        var totalProperty = captured.GetType().GetProperty("Total");
        if (totalProperty is not null)
        {
            Assert.Equal(
                Convert.ChangeType(42, totalProperty.PropertyType, CultureInfo.InvariantCulture),
                totalProperty.GetValue(captured));
        }
    }

    [Fact]
    public void AsyncPSCmdlet_snapshots_captured_errors_before_queueing()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCapturedErrorSnapshot",
            typeof(TestAsyncCapturedErrorSnapshotCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCapturedErrorSnapshot");

        var result = powerShell.Invoke();

        Assert.Equal("completed", Assert.Single(result).BaseObject);
        var captured = Assert.Single(powerShell.Streams.Error);
        Assert.Equal("original details", captured.ErrorDetails?.Message);
    }

    [Fact]
    public void AsyncPSCmdlet_snapshots_terminating_errors_before_queueing()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncTerminatingErrorSnapshot",
            typeof(TestAsyncTerminatingErrorSnapshotCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncTerminatingErrorSnapshot");

        var exception = Assert.Throws<CmdletInvocationException>(
            () => powerShell.Invoke());

        Assert.Equal(
            "original terminating details",
            exception.ErrorRecord.ErrorDetails?.Message);
    }

    [Fact]
    public void AsyncPSCmdlet_rejects_context_callbacks_from_an_earlier_hook()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncStaleProgress",
            typeof(TestAsyncStaleProgressCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddScript("1, 2 | Test-AsyncStaleProgress");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors);
        Assert.Equal(2, Assert.Single(result).BaseObject);
        Assert.Empty(powerShell.Streams.Warning);
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_causal_stream_records_before_enumerator_failure()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncEnumeratorFailure",
            typeof(TestAsyncEnumeratorFailureCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncEnumeratorFailure");

        var exception = Record.Exception(() => powerShell.Invoke());

        Assert.NotNull(exception);
        Assert.Equal(
            "before enumeration failure",
            Assert.Single(powerShell.Streams.Warning).Message);
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_pipeline_failure_when_cancellation_callbacks_throw()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncPipelineFailureWithThrowingCancellation",
            typeof(TestAsyncPipelineFailureWithThrowingCancellationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncPipelineFailureWithThrowingCancellation");

        var exception = Assert.Throws<CmdletInvocationException>(() => powerShell.Invoke());

        Assert.StartsWith("PipelineFailure,", exception.ErrorRecord.FullyQualifiedErrorId, StringComparison.Ordinal);
        Assert.Equal("pipeline failure", exception.InnerException?.Message);
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

        Assert.True(
            TestAsyncDisposeDuringHookCommand.TokenRead.Wait(TimeSpan.FromSeconds(5)),
            "The asynchronous hook did not finish reading the token in time.");
        Assert.True(TestAsyncDisposeDuringHookCommand.ReadTokenAfterDispose);
        Assert.Null(TestAsyncDisposeDuringHookCommand.TokenReadException);
    }

    [Fact]
    public void AsyncPSCmdlet_keeps_the_cancellation_source_alive_until_Cancel_returns()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDisposeDuringCancellation",
            typeof(TestAsyncDisposeDuringCancellationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncDisposeDuringCancellation");
        TestAsyncDisposeDuringCancellationCommand.Reset();

        powerShell.Invoke();

        Assert.True(
            TestAsyncDisposeDuringCancellationCommand.CallbackCompleted.Wait(TimeSpan.FromSeconds(5)),
            "The cancellation callback did not complete in time.");
        Assert.True(TestAsyncDisposeDuringCancellationCommand.TokenReadDuringCancellation);
        Assert.Null(TestAsyncDisposeDuringCancellationCommand.TokenReadException);
    }

    [Fact]
    public void AsyncPSCmdlet_cancels_started_work_when_a_direct_write_stops_the_pipeline()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncDirectPipelineStop",
            typeof(TestAsyncDirectPipelineStopCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncDirectPipelineStop")
            .AddCommand("Select-Object")
            .AddParameter("First", 1);
        TestAsyncDirectPipelineStopCommand.Reset();

        var result = powerShell.Invoke();

        Assert.Equal(1, Assert.Single(result).BaseObject);
        Assert.True(
            TestAsyncDirectPipelineStopCommand.CancellationObserved.Wait(TimeSpan.FromSeconds(5)),
            "The started operation was not canceled after the pipeline stopped.");
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_cancel_after_successful_async_completion()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncSuccessfulCompletion",
            typeof(TestAsyncSuccessfulCompletionCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncSuccessfulCompletion");
        TestAsyncSuccessfulCompletionCommand.Reset();

        var result = powerShell.Invoke();

        Assert.Equal("completed", Assert.Single(result).BaseObject);
        Assert.False(TestAsyncSuccessfulCompletionCommand.CancellationObserved.IsSet);
    }

    [Fact]
    public void AsyncPSCmdlet_suppresses_pipeline_stop_from_posted_progress_callbacks()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncLateProgress",
            typeof(TestAsyncLateProgressCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncLateProgress");
        TestAsyncLateProgressCommand.Reset();

        var invocation = powerShell.BeginInvoke();
        Assert.True(
            TestAsyncLateProgressCommand.Initialized.Wait(TimeSpan.FromSeconds(5)),
            "The progress callback was not initialized in time.");
        powerShell.Stop();
        Assert.Throws<PipelineStoppedException>(() => powerShell.EndInvoke(invocation));

        TestAsyncLateProgressCommand.ReportAfterStop();
        TestAsyncLateProgressCommand.WriteAfterStop();

        Assert.True(
            TestAsyncLateProgressCommand.CallbackCompleted.Wait(TimeSpan.FromSeconds(5)),
            "The posted progress callback did not complete in time.");
        Assert.Null(TestAsyncLateProgressCommand.LateWriteException);
    }

    [Fact]
    public void AsyncPSCmdlet_normalizes_synchronous_cancellation_after_stop()
    {
        using var command = new TestAsyncSynchronousStopCommand();

        Assert.Throws<PipelineStoppedException>(command.InvokeProcessRecord);
    }

    [Fact]
    public void AsyncPSCmdlet_does_not_invoke_a_hook_after_stop()
    {
        using var command = new TestAsyncStoppedBeforeHookCommand();
        command.InvokeStopProcessing();

        Assert.Throws<PipelineStoppedException>(command.InvokeProcessRecord);
        Assert.False(command.HookInvoked);
    }
}
