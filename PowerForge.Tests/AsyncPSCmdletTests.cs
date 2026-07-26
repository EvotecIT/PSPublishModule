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
    public void AsyncPSCmdlet_drops_background_stream_writes_during_pipeline_stop()
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

        Assert.Null(TestAsyncCancellationWriteCommand.BackgroundWriteException);
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

        Assert.True(
            TestAsyncDisposeDuringHookCommand.TokenRead.Wait(TimeSpan.FromSeconds(5)),
            "The asynchronous hook did not finish reading the token in time.");
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
