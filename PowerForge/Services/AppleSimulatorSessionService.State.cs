using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleSimulatorSessionService
{
    /// <summary>Default coordination root shared by cooperating sessions running as the current user.</summary>
    public static string DefaultStateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerForge", "AppleSimulator");

    /// <summary>Reconciles the last receipt without shutting down devices or changing stored state.</summary>
    /// <param name="stateRoot">Shared coordination root, or null for this user's default.</param>
    /// <param name="cancellationToken">Bounds caller cancellation of simulator discovery.</param>
    /// <returns>Owner identity, current device state, and whether the receipt needs attention.</returns>
    public async Task<AppleSimulatorSessionStatus> InspectAsync(string? stateRoot = null, CancellationToken cancellationToken = default)
    {
        var root = ResolveStateRoot(stateRoot);
        var receipt = ReadReceipt(root);
        var status = new AppleSimulatorSessionStatus { Receipt = receipt, ReceiptPath = ReceiptPath(root) };
        if (receipt is null) return status;
        status.OwnerState = OwnerState(receipt);
        status.CommandState = ProcessState(receipt.CommandProcessId, receipt.CommandProcessStartedUtc);
        status.NeedsAttention = NeedsAttention(receipt);
        try
        {
            status.DeviceState = await GetDeviceStateAsync(receipt.DeviceId, receipt.DeviceSetPath, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            status.DeviceState = "Unknown";
            status.DiscoveryError = exception.Message;
            status.NeedsAttention = true;
        }
        return status;
    }

    /// <summary>Acknowledges an interrupted receipt only after owner exit and a safely released/preserved device are verified.</summary>
    /// <remarks>Never shuts down a simulator. The operator must resolve ambiguous ownership before invoking this action.</remarks>
    /// <param name="sessionId">Exact outstanding session identifier, preventing acknowledgment of a different run.</param>
    /// <param name="stateRoot">Shared coordination root, or null for this user's default.</param>
    /// <param name="cancellationToken">Cancellation of read-only simulator verification.</param>
    /// <returns>The reconciled receipt.</returns>
    public async Task<AppleSimulatorSessionReceipt> AcknowledgeAsync(string sessionId, string? stateRoot = null, CancellationToken cancellationToken = default)
    {
        var root = ResolveStateRoot(stateRoot);
        using var sessionLock = AcquireLock(root);
        var receipt = ReadReceipt(root) ?? throw new InvalidOperationException("There is no simulator session to acknowledge.");
        if (!string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal)) throw new InvalidOperationException("Session identifier does not match the current receipt.");
        if (!NeedsAttention(receipt)) throw new InvalidOperationException("This session does not need acknowledgment.");
        if (OwnerState(receipt) != "exited") throw new InvalidOperationException("Session owner has not been verified exited; preserve its device.");
        if (ProcessState(receipt.CommandProcessId, receipt.CommandProcessStartedUtc) is "active" or "unknown")
            throw new InvalidOperationException("Recorded validation process has not been verified exited; preserve its session.");
        var state = await GetDeviceStateAsync(receipt.DeviceId, receipt.DeviceSetPath, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (state != "Shutdown" && !(state == "Booted" && receipt.InitialState == "Booted" && !receipt.BootRequested))
            throw new InvalidOperationException("Owned or ambiguous simulator boot must be independently resolved to Shutdown before acknowledgment.");
        receipt.FinalState = state;
        receipt.CleanupSucceeded = true;
        receipt.CompletedAtUtc = DateTimeOffset.UtcNow;
        receipt.Outcome = "acknowledged";
        WriteReceipt(root, receipt);
        return receipt;
    }

    private static bool NeedsAttention(AppleSimulatorSessionReceipt receipt) => receipt.CompletedAtUtc is null || !receipt.CleanupSucceeded;

    private static string OwnerState(AppleSimulatorSessionReceipt receipt)
        => ProcessState(receipt.OwnerProcessId, receipt.OwnerProcessStartedUtc);

    private static string ProcessState(int? processId, DateTime? startedUtc)
    {
        if (processId is null) return "none";
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (process.HasExited) return "exited";
            if (startedUtc is null) return "unknown";
            return !process.HasExited && process.StartTime.ToUniversalTime() == startedUtc ? "active" : "exited";
        }
        catch (ArgumentException) { return "exited"; }
        catch (InvalidOperationException) { return "exited"; }
        catch { return "unknown"; }
    }

    private static string ResolveStateRoot(string? path)
    {
        if (path is not null && string.IsNullOrWhiteSpace(path)) throw new ArgumentException("State root cannot be empty.");
        return FileSystemPathSafety.ResolveParentDirectoryAliases(path ?? DefaultStateRoot);
    }

    private static string ReceiptPath(string root) => Path.Combine(root, "session.json");

    private static FileStream AcquireLock(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.lock");
        FileSystemPathSafety.RejectReparsePoints(path, root, "Simulator session lock");
        if (File.Exists(path)) FileSystemPathSafety.RequireRegularFile(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new InvalidOperationException("Another cooperating simulator session owns this coordination root. Wait for it to finish; do not delete its lock file.", exception); }
    }

    private static AppleSimulatorSessionReceipt? ReadReceipt(string root)
    {
        var path = ReceiptPath(root);
        FileSystemPathSafety.RejectReparsePoints(path, root, "Simulator session receipt");
        if (!File.Exists(path)) return null;
        FileSystemPathSafety.RequireRegularFile(path);
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Simulator ownership receipt exceeds its size limit.");
        var receipt = JsonSerializer.Deserialize(File.ReadAllText(path), AppleSimulatorJsonContext.Default.AppleSimulatorSessionReceipt)
            ?? throw new InvalidDataException("Simulator ownership receipt is empty.");
        if (receipt.SchemaVersion != 1 || !Guid.TryParseExact(receipt.SessionId, "D", out _) || receipt.OwnerProcessId <= 0 ||
            receipt.OwnerProcessStartedUtc.Kind != DateTimeKind.Utc || receipt.InitialState is not ("Booted" or "Shutdown"))
            throw new InvalidDataException("Simulator ownership receipt is invalid; preserve it for inspection.");
        NormalizeDeviceId(receipt.DeviceId);
        if (receipt.DeviceSetPath is not null && !Path.IsPathRooted(receipt.DeviceSetPath))
            throw new InvalidDataException("Simulator receipt device set must be absolute.");
        return receipt;
    }

    private static void WriteReceipt(string root, AppleSimulatorSessionReceipt receipt)
    {
        var path = ReceiptPath(root);
        FileSystemPathSafety.RejectReparsePoints(path, root, "Simulator session receipt");
        var temporary = Path.Combine(root, "receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, receipt, AppleSimulatorJsonContext.Default.AppleSimulatorSessionReceipt);
                stream.Flush(flushToDisk: true);
            }
#if NET472
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
#else
            File.Move(temporary, path, overwrite: true);
#endif
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
