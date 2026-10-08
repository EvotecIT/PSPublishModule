using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class AppleSimulatorSessionServiceTests
{
    private const string Device = "11111111-1111-1111-1111-111111111111";
    private const string OtherDevice = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(124, true)]
    [InlineData(127, false)]
    public async Task Run_releases_owned_boot_and_preserves_command_outcome(int exitCode, bool timedOut)
    {
        using var fixture = new Fixture();
        fixture.Validation = (_, _) => Task.FromResult(Result(exitCode, "child output", timedOut));
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(exitCode == 0, result.Success);
        Assert.True(result.Receipt.BootSucceeded);
        Assert.True(result.Receipt.CleanupSucceeded);
        Assert.Equal("Shutdown", fixture.Devices[Device]);
        Assert.Equal("Booted", fixture.Devices[OtherDevice]);
        Assert.Equal("Shutdown", result.Receipt.FinalState);
        Assert.Equal("child output", result.CommandResult!.StdOut);
        var persisted = File.ReadAllText(result.ReceiptPath);
        Assert.DoesNotContain("child output", persisted);
        Assert.DoesNotContain("sensitive-command-argument", persisted);
        Assert.DoesNotContain("sensitive-environment", persisted);
        Assert.All(fixture.SimulatorRequests, request => Assert.Equal(new[] { "simctl", "--set", fixture.DeviceSet }, request.Arguments.Take(3)));
    }

    [Fact]
    public async Task Bounded_validation_log_truncation_does_not_turn_a_successful_command_into_failure()
    {
        using var fixture = new Fixture();
        fixture.Validation = (_, _) => Task.FromResult(new ProcessRunResult(0, "bounded", "", "fixture", TimeSpan.Zero,
            timedOut: false, standardOutputLimitExceeded: true));
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.CommandResult!.StandardOutputLimitExceeded);
    }

    [Fact]
    public async Task Run_preserves_a_preexisting_booted_device_without_requesting_another_boot()
    {
        using var fixture = new Fixture();
        fixture.Devices[Device] = "Booted";
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.True(result.Success);
        Assert.False(result.Receipt.BootRequested);
        Assert.Equal("Booted", fixture.Devices[Device]);
        Assert.DoesNotContain(fixture.SimulatorRequests, request => request.Arguments[3] is "boot" or "shutdown");
        Assert.DoesNotContain(fixture.SimulatorRequests, request => request.Arguments.Contains("-b"));
    }

    [Fact]
    public async Task Cancellation_uses_an_independent_cleanup_token_and_blocks_overlap()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Validation = async (_, token) => { started.SetResult(true); await Task.Delay(Timeout.Infinite, token); return Result(); };
        var first = fixture.Service.RunAsync(fixture.Request(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RunAsync(fixture.Request()));
        Assert.Contains("Another cooperating simulator session", exception.Message);
        cancellation.Cancel();
        var result = await first;
        Assert.Equal(130, result.ExitCode);
        Assert.Equal("canceled", result.Receipt.Outcome);
        Assert.True(result.Receipt.CleanupSucceeded);
        Assert.Equal("Shutdown", fixture.Devices[Device]);
    }

    [Theory]
    [InlineData(0, 130)]
    [InlineData(7, 7)]
    public async Task Cancellation_observed_after_command_result_cannot_report_success(int childExitCode, int expectedExitCode)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Validation = (_, _) => { cancellation.Cancel(); return Task.FromResult(Result(childExitCode)); };
        var result = await fixture.Service.RunAsync(fixture.Request(), cancellation.Token);
        Assert.Equal("canceled", result.Receipt.Outcome);
        Assert.False(result.Success);
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.True(result.Receipt.CleanupSucceeded);
        Assert.Equal("Shutdown", fixture.Devices[Device]);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task Status_preserves_interrupted_ownership_evidence_when_simulator_discovery_fails(string failure)
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.RunAsync(fixture.Request());
        result.Receipt.OwnerProcessStartedUtc = result.Receipt.OwnerProcessStartedUtc.AddTicks(-1);
        result.Receipt.CompletedAtUtc = null;
        File.WriteAllText(result.ReceiptPath, JsonSerializer.Serialize(result.Receipt));
        var before = File.ReadAllText(result.ReceiptPath);
        fixture.DiscoveryFailure = failure;
        var status = await fixture.Service.InspectAsync(fixture.StateRoot);
        Assert.Equal(result.Receipt.SessionId, status.Receipt!.SessionId);
        Assert.Equal("exited", status.OwnerState);
        Assert.Equal("none", status.CommandState);
        Assert.True(status.NeedsAttention);
        Assert.Equal("Unknown", status.DeviceState);
        Assert.NotEmpty(status.DiscoveryError!);
        Assert.Equal(before, File.ReadAllText(result.ReceiptPath));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.AcknowledgeAsync(result.Receipt.SessionId, fixture.StateRoot));
    }

    [Fact]
    public async Task Readiness_failure_releases_a_confirmed_boot_without_running_validation()
    {
        using var fixture = new Fixture { FailReadiness = true };
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.False(result.Success);
        Assert.Null(result.CommandResult);
        Assert.True(result.Receipt.CleanupSucceeded);
        Assert.Equal("Shutdown", fixture.Devices[Device]);
    }

    [Fact]
    public async Task Ambiguous_boot_failure_is_reported_and_does_not_shutdown_another_actors_boot()
    {
        using var fixture = new Fixture { FailBootAfterStateChange = true };
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.False(result.Success);
        Assert.False(result.Receipt.BootSucceeded);
        Assert.False(result.Receipt.CleanupSucceeded);
        Assert.Equal("Booted", fixture.Devices[Device]);
        Assert.DoesNotContain(fixture.SimulatorRequests, request => request.Arguments[3] == "shutdown");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RunAsync(fixture.Request()));
    }

    [Fact]
    public async Task Cleanup_failure_remains_visible_and_cannot_be_overwritten_or_acknowledged_while_owner_is_live()
    {
        using var fixture = new Fixture { FailShutdown = true };
        var result = await fixture.Service.RunAsync(fixture.Request());
        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.True(result.CommandResult!.Succeeded);
        Assert.NotNull(result.CleanupError);
        Assert.False(result.Receipt.CleanupSucceeded);
        var before = File.ReadAllText(result.ReceiptPath);
        var status = await fixture.Service.InspectAsync(fixture.StateRoot);
        Assert.True(status.NeedsAttention);
        Assert.Equal("active", status.OwnerState);
        Assert.Equal("Booted", status.DeviceState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RunAsync(fixture.Request()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcknowledgeAsync(result.Receipt.SessionId, fixture.StateRoot));
        Assert.Equal(before, File.ReadAllText(result.ReceiptPath));
    }

    [Fact]
    public async Task Interrupted_receipt_requires_exact_session_exited_identity_and_shutdown_before_acknowledgment()
    {
        using var fixture = new Fixture { FailShutdown = true };
        var result = await fixture.Service.RunAsync(fixture.Request());
        // A reused PID with a different start identity is not the recorded owner.
        result.Receipt.OwnerProcessStartedUtc = result.Receipt.OwnerProcessStartedUtc.AddTicks(-1);
        result.Receipt.CompletedAtUtc = null;
        File.WriteAllText(result.ReceiptPath, JsonSerializer.Serialize(result.Receipt));
        Assert.Equal("exited", (await fixture.Service.InspectAsync(fixture.StateRoot)).OwnerState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcknowledgeAsync(Guid.NewGuid().ToString("D"), fixture.StateRoot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcknowledgeAsync(result.Receipt.SessionId, fixture.StateRoot));
        fixture.Devices[Device] = "Shutdown";
        using var process = Process.GetCurrentProcess();
        result.Receipt.CommandProcessId = process.Id;
        result.Receipt.CommandProcessStartedUtc = process.StartTime.ToUniversalTime();
        File.WriteAllText(result.ReceiptPath, JsonSerializer.Serialize(result.Receipt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcknowledgeAsync(result.Receipt.SessionId, fixture.StateRoot));
        result.Receipt.CommandProcessStartedUtc = result.Receipt.CommandProcessStartedUtc!.Value.AddTicks(-1);
        File.WriteAllText(result.ReceiptPath, JsonSerializer.Serialize(result.Receipt));
        var acknowledged = await fixture.Service.AcknowledgeAsync(result.Receipt.SessionId, fixture.StateRoot);
        Assert.True(acknowledged.CleanupSucceeded);
        Assert.Equal("acknowledged", acknowledged.Outcome);
        Assert.False((await fixture.Service.InspectAsync(fixture.StateRoot)).NeedsAttention);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("booted")]
    [InlineData("iPhone")]
    public async Task Broad_or_ambiguous_targets_are_rejected_before_simulator_commands(string target)
    {
        using var fixture = new Fixture();
        var request = fixture.Request(); request.DeviceId = target;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RunAsync(request));
        Assert.Empty(fixture.SimulatorRequests);
    }

    private static ProcessRunResult Result(int exitCode = 0, string output = "", bool timedOut = false)
        => new(exitCode, output, "", "fixture", TimeSpan.Zero, timedOut);

    private sealed class DelegateRunner(Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> run) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => run(request, cancellationToken);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Simulator-" + Guid.NewGuid().ToString("N"));
        internal string StateRoot => Path.Combine(_root, "state");
        internal string DeviceSet => Path.Combine(_root, "device set");
        internal Dictionary<string, string> Devices { get; } = new() { [Device] = "Shutdown", [OtherDevice] = "Booted" };
        internal List<ProcessRunRequest> SimulatorRequests { get; } = new();
        internal Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> Validation { get; set; } = (_, _) => Task.FromResult(Result());
        internal bool FailBootAfterStateChange { get; init; }
        internal bool FailReadiness { get; init; }
        internal bool FailShutdown { get; init; }
        internal string? DiscoveryFailure { get; set; }
        internal AppleSimulatorSessionService Service { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(DeviceSet);
            Service = new AppleSimulatorSessionService(new DelegateRunner(Simulator), new DelegateRunner((request, token) => Validation(request, token)));
        }

        internal AppleSimulatorSessionRequest Request() => new()
        {
            DeviceId = Device, Owner = "contract-test", StateRoot = StateRoot, DeviceSetPath = DeviceSet,
            Command = new ProcessRunRequest("fixture-validation", _root, new[] { "sensitive-command-argument" }, TimeSpan.FromSeconds(10),
                new Dictionary<string, string?> { ["SECRET"] = "sensitive-environment" })
        };

        private Task<ProcessRunResult> Simulator(ProcessRunRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            SimulatorRequests.Add(request);
            var command = request.Arguments[3];
            if (command == "list")
            {
                if (DiscoveryFailure == "failed") return Task.FromResult(Result(1));
                if (DiscoveryFailure == "malformed") return Task.FromResult(Result(output: "invalid JSON"));
                if (DiscoveryFailure == "missing") return Task.FromResult(Result(output: "{\"devices\":{}}"));
                var devices = Devices.Select(device => new { udid = device.Key, state = device.Value, isAvailable = true }).ToArray();
                return Task.FromResult(Result(output: JsonSerializer.Serialize(new { devices = new Dictionary<string, object> { ["iOS-fixture"] = devices } })));
            }
            if (command == "boot") { Devices[Device] = "Booted"; return Task.FromResult(Result(FailBootAfterStateChange ? 1 : 0)); }
            if (command == "bootstatus") return Task.FromResult(Result(FailReadiness ? 1 : 0));
            if (command == "shutdown") { if (!FailShutdown) Devices[Device] = "Shutdown"; return Task.FromResult(Result(FailShutdown ? 1 : 0)); }
            throw new InvalidOperationException("Unexpected simulator command.");
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
