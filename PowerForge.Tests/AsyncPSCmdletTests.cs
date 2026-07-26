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
    public void AsyncPSCmdlet_preserves_fifo_order_for_pipeline_thread_and_worker_writes()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncWriteOrdering",
            typeof(TestAsyncWriteOrderingCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncWriteOrdering");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        Assert.Collection(
            result,
            item => Assert.Equal("first", item.BaseObject),
            item => Assert.Equal("second", item.BaseObject));
    }

    [Fact]
    public void AsyncPSCmdlet_snapshots_information_tags_before_queueing()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncInformationTags",
            typeof(TestAsyncInformationTagsCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncInformationTags");

        powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var record = Assert.Single(powerShell.Streams.Information);
        Assert.Equal("message", record.MessageData);
        Assert.Equal(["before"], record.Tags);
    }

    [Fact]
    public void AsyncPSCmdlet_applies_stopping_preferences_before_the_hook_continues()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncSynchronousError",
            typeof(TestAsyncSynchronousErrorCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncSynchronousError")
            .AddParameter("ErrorAction", ActionPreference.Stop);
        TestAsyncSynchronousErrorCommand.Reset();

        _ = Assert.ThrowsAny<RuntimeException>(() => powerShell.Invoke());

        Assert.False(TestAsyncSynchronousErrorCommand.ReachedAfterError);
    }

    [Fact]
    public void AsyncPSCmdlet_enumerates_pipeline_thread_collections_before_the_hook_mutates_them()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncSynchronousEnumeration",
            typeof(TestAsyncSynchronousEnumerationCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncSynchronousEnumeration");

        var result = powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        Assert.Collection(
            result,
            item => Assert.Equal(1, item.BaseObject),
            item => Assert.Equal(2, item.BaseObject));
    }

    [Fact]
    public void AsyncPSCmdlet_drains_worker_records_before_a_synchronous_hook_failure()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncSynchronousFailure",
            typeof(TestAsyncSynchronousFailureCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncSynchronousFailure");

        _ = Assert.ThrowsAny<RuntimeException>(() => powerShell.Invoke());

        var warning = Assert.Single(powerShell.Streams.Warning);
        Assert.Equal("before-failure", warning.Message);
    }

    [Fact]
    public void AsyncPSCmdlet_preserves_null_information_tags_after_await()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncNullInformationTags",
            typeof(TestAsyncNullInformationTagsCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncNullInformationTags");

        powerShell.Invoke();

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        var record = Assert.Single(powerShell.Streams.Information);
        Assert.Equal("untagged", record.MessageData);
        Assert.Empty(record.Tags);
    }

    [Fact]
    public void AsyncPSCmdlet_rejects_interactions_from_an_older_record_lifecycle()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncStaleInteraction",
            typeof(TestAsyncStaleInteractionCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell
            .AddCommand("Test-AsyncStaleInteraction")
            .AddParameter("Confirm", false);
        TestAsyncStaleInteractionCommand.Reset();

        powerShell.Invoke(new[] { "first", "second" });

        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString())));
        Assert.IsType<InvalidOperationException>(TestAsyncStaleInteractionCommand.StaleInteractionException);
        Assert.DoesNotContain(powerShell.Streams.Warning, static warning => warning.Message == "stale-warning");
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
    public void AsyncPSCmdlet_propagates_pipeline_stop_to_background_stream_writes()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCancellationWrite",
            typeof(TestAsyncCancellationWriteCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCancellationWrite");
        TestAsyncCancellationWriteCommand.Reset();

        var invocation = powerShell.BeginInvoke();
        Assert.True(
            TestAsyncCancellationWriteCommand.Started.Wait(TimeSpan.FromSeconds(5)),
            "The asynchronous cmdlet did not start in time.");

        powerShell.Stop();
        Assert.Throws<PipelineStoppedException>(() => powerShell.EndInvoke(invocation));
        Assert.True(
            TestAsyncCancellationWriteCommand.WriteAttempted.Wait(TimeSpan.FromSeconds(5)),
            "The background hook did not observe the stop in time.");

        Assert.IsType<PipelineStoppedException>(
            TestAsyncCancellationWriteCommand.BackgroundWriteException);
    }

    [Fact]
    public void AsyncPSCmdlet_drops_captured_callback_writes_after_pipeline_stop()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncCapturedCancellationWrite",
            typeof(TestAsyncCapturedCancellationWriteCommand),
            helpFileName: null));

        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncCapturedCancellationWrite");
        TestAsyncCapturedCancellationWriteCommand.Reset();

        var invocation = powerShell.BeginInvoke();
        Assert.True(
            TestAsyncCapturedCancellationWriteCommand.Started.Wait(TimeSpan.FromSeconds(5)),
            "The asynchronous cmdlet did not start in time.");

        powerShell.Stop();
        Assert.Throws<PipelineStoppedException>(() => powerShell.EndInvoke(invocation));
        Assert.True(
            TestAsyncCapturedCancellationWriteCommand.WriteAttempted.Wait(TimeSpan.FromSeconds(5)),
            "The captured callback did not observe the stop in time.");
        Assert.Null(TestAsyncCapturedCancellationWriteCommand.BackgroundWriteException);
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
    public void AsyncPSCmdlet_returns_host_interaction_failures_to_the_requesting_worker()
    {
        var sessionState = InitialSessionState.Create();
        sessionState.Commands.Add(new SessionStateCmdletEntry(
            "Test-AsyncShouldContinue",
            typeof(TestAsyncShouldContinueCommand),
            helpFileName: null));

        var promptFailure = new InvalidOperationException("host prompt failed");
        using var runspace = RunspaceFactory.CreateRunspace(new ChoiceHost(approved: false, promptFailure), sessionState);
        runspace.Open();
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Test-AsyncShouldContinue");

        var exception = Assert.Throws<CmdletInvocationException>(() => powerShell.Invoke());

        Assert.Same(promptFailure, exception.InnerException);
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
    public void AsyncPSCmdlet_accepts_context_free_callbacks_while_their_lazy_item_is_pumped()
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
            Assert.Single(powerShell.Streams.Warning).Message);
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

        Assert.Equal("derived-end", Assert.Single(result).BaseObject);
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

        Assert.True(
            TestAsyncLateProgressCommand.CallbackCompleted.Wait(TimeSpan.FromSeconds(5)),
            "The posted progress callback did not complete in time.");
    }

    [Fact]
    public void AsyncPSCmdlet_normalizes_synchronous_cancellation_after_stop()
    {
        using var command = new TestAsyncSynchronousStopCommand();

        Assert.Throws<PipelineStoppedException>(command.InvokeProcessRecord);
    }
}
