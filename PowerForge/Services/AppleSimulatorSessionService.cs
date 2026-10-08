using System.Diagnostics;

namespace PowerForge;

/// <summary>Coordinates cooperating simulator sessions and releases only devices booted by that session.</summary>
/// <remarks>Does not erase devices, reset simulator services, configure runners, or automatically recover abandoned sessions.</remarks>
public sealed partial class AppleSimulatorSessionService
{
    private readonly IProcessRunner _simulatorRunner;
    private readonly IProcessRunner _commandRunner;

    /// <summary>Creates a lifecycle owner with existing process-runner boundaries.</summary>
    /// <param name="simulatorRunner">Simulator command executor; defaults to the standard runner.</param>
    /// <param name="commandRunner">Foreground validation executor; defaults to a runner owning its descendant scope.</param>
    public AppleSimulatorSessionService(IProcessRunner? simulatorRunner = null, IProcessRunner? commandRunner = null)
    {
        _simulatorRunner = simulatorRunner ?? new ProcessRunner();
        _commandRunner = commandRunner ?? new ProcessRunner(ownProcessTree: true);
    }

    /// <summary>Runs bounded validation, preserving pre-existing booted devices and independently attempting owned shutdown.</summary>
    /// <param name="request">Exact device, owner, finite command, and shared coordination directory.</param>
    /// <param name="cancellationToken">Cancels validation; release uses its own bounded token.</param>
    /// <returns>Both validation and release outcomes, with a compact durable receipt.</returns>
    public async Task<AppleSimulatorSessionResult> RunAsync(AppleSimulatorSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var deviceId = NormalizeDeviceId(request.DeviceId);
        if (string.IsNullOrWhiteSpace(request.Owner) || request.Owner.Length > 200 || request.Owner.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("A single-line owner reference of at most 200 characters is required.");
        var command = request.Command ?? throw new ArgumentException("A foreground command is required.");
        RequireFiniteTimeout(command.Timeout, "Command timeout");
        RequireFiniteTimeout(request.SimulatorTimeout, "Simulator timeout");
        RequireFiniteTimeout(request.CleanupTimeout, "Cleanup timeout");
        var deviceSet = NormalizeDeviceSet(request.DeviceSetPath);
        var root = ResolveStateRoot(request.StateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        using var sessionLock = AcquireLock(root);
        var previous = ReadReceipt(root);
        if (previous is not null && NeedsAttention(previous))
            throw new InvalidOperationException($"Simulator session {previous.SessionId} needs attention. Inspect status and acknowledge only after its owner exited and its exact device is Shutdown.");

        var initial = await GetDeviceStateAsync(deviceId, deviceSet, request.SimulatorTimeout, cancellationToken).ConfigureAwait(false);
        if (initial is not ("Shutdown" or "Booted"))
            throw new InvalidOperationException($"Simulator {deviceId} is {initial}; wait for a stable Shutdown or Booted state.");
        using var owner = Process.GetCurrentProcess();
        var receipt = new AppleSimulatorSessionReceipt
        {
            SessionId = Guid.NewGuid().ToString("D"), Owner = request.Owner,
            OwnerProcessId = owner.Id, OwnerProcessStartedUtc = owner.StartTime.ToUniversalTime(),
            StartedAtUtc = DateTimeOffset.UtcNow, DeviceId = deviceId, DeviceSetPath = deviceSet,
            InitialState = initial
        };
        var result = new AppleSimulatorSessionResult { Receipt = receipt, ReceiptPath = ReceiptPath(root) };
        WriteReceipt(root, receipt);
        try
        {
            if (initial == "Shutdown")
            {
                receipt.BootRequested = true;
                WriteReceipt(root, receipt);
                var boot = await SimctlAsync(deviceSet, new[] { "boot", deviceId }, request.SimulatorTimeout, cancellationToken).ConfigureAwait(false);
                receipt.BootSucceeded = boot.Succeeded;
                WriteReceipt(root, receipt);
                RequireSuccess(boot, "Simulator boot");
            }
            RequireSuccess(await SimctlAsync(deviceSet, new[] { "bootstatus", deviceId }, request.SimulatorTimeout, cancellationToken).ConfigureAwait(false), "Simulator readiness");
            cancellationToken.ThrowIfCancellationRequested();
            var foreground = new ProcessRunRequest(command.FileName, command.WorkingDirectory, command.Arguments,
                command.Timeout, command.EnvironmentVariables, command.CaptureOutput, command.CaptureError,
                command.OutputLineReceived, command.ErrorLineReceived, command.InheritEnvironment)
            {
                MaxCapturedOutputCharacters = Math.Min(command.MaxCapturedOutputCharacters, 16384),
                RequireDirectStart = command.RequireDirectStart
            };
            foreground.SetStartedProcessBoundary(pid =>
            {
                receipt.CommandProcessId = pid;
                try
                {
                    using var child = Process.GetProcessById(pid);
                    receipt.CommandProcessStartedUtc = child.StartTime.ToUniversalTime();
                }
                catch (ArgumentException) { /* A short-lived child may already have exited. */ }
                catch (InvalidOperationException) { /* Preserve the PID with unknown identity for crash reconciliation. */ }
                WriteReceipt(root, receipt);
            });
            result.CommandResult = await _commandRunner.RunAsync(foreground, cancellationToken).ConfigureAwait(false);
            receipt.CommandExitCode = result.CommandResult.ExitCode;
            receipt.Outcome = cancellationToken.IsCancellationRequested ? "canceled" : result.ValidationSucceeded ? "succeeded" : "failed";
        }
        catch (OperationCanceledException)
        {
            receipt.Outcome = "canceled";
            result.Error = "Simulator validation canceled.";
        }
        catch (Exception exception)
        {
            receipt.Outcome = cancellationToken.IsCancellationRequested ? "canceled" : "failed";
            result.Error = exception.Message;
        }
        finally
        {
            await ReleaseAsync(receipt, request.CleanupTimeout, result).ConfigureAwait(false);
            receipt.CompletedAtUtc = DateTimeOffset.UtcNow;
            if (!receipt.CleanupSucceeded && receipt.Outcome == "succeeded") receipt.Outcome = "cleanup-incomplete";
            WriteReceipt(root, receipt);
        }
        return result;
    }

    private async Task ReleaseAsync(AppleSimulatorSessionReceipt receipt, TimeSpan timeout, AppleSimulatorSessionResult result)
    {
        using var cleanup = new CancellationTokenSource(timeout);
        try
        {
            if (receipt.BootSucceeded)
            {
                // A failed boot is ambiguous: another actor may have booted this device.
                // Only our positively confirmed boot grants automatic shutdown authority.
                var state = await GetDeviceStateAsync(receipt.DeviceId, receipt.DeviceSetPath, timeout, cleanup.Token).ConfigureAwait(false);
                if (state != "Shutdown")
                    RequireSuccess(await SimctlAsync(receipt.DeviceSetPath, new[] { "shutdown", receipt.DeviceId }, timeout, cleanup.Token).ConfigureAwait(false), "Simulator shutdown");
            }
            receipt.FinalState = await GetDeviceStateAsync(receipt.DeviceId, receipt.DeviceSetPath, timeout, cleanup.Token).ConfigureAwait(false);
            receipt.CleanupSucceeded = !receipt.BootRequested || receipt.FinalState == "Shutdown";
            if (!receipt.CleanupSucceeded)
                result.CleanupError = "Simulator shutdown was not verified. Inspect the retained receipt; ambiguous boot attempts are not automatically shut down.";
        }
        catch (Exception exception)
        {
            receipt.FinalState = "Unknown";
            receipt.CleanupSucceeded = false;
            result.CleanupError = exception.Message;
        }
    }
}
