using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Machine-local app ownership and delivery reservations shared by every checkout.</summary>
internal sealed class AppleReleaseAppState : IDisposable
{
    private readonly List<FileStream> _leases = new();
    private readonly string[] _directories;

    internal static string Root => ResolveRoot();

    private AppleReleaseAppState(string[] directories) => _directories = directories;

    internal static AppleReleaseAppState Acquire(PowerForgeAppleReleasePlan plan, string? stateRoot = null)
    {
        var directories = Directories(plan, stateRoot);
        var state = new AppleReleaseAppState(directories);
        try
        {
            foreach (var directory in directories)
            {
                EnsureDirectory(directory);
                var path = Path.Combine(directory, "operation.lock");
                AssertUnlinked(path);
                try { state._leases.Add(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
                catch (IOException exception) when (IsLockSharingConflict(exception))
                { throw new AppleReleaseLeaseContentionException(exception); }
            }
            return state;
        }
        catch { state.Dispose(); throw; }
    }

    internal static long HighestReserved(PowerForgeAppleReleasePlan plan, string? source, long current, string? stateRoot = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return 0;
        long highest = 0;
        foreach (var directory in Directories(plan, stateRoot))
        {
            foreach (var reservation in ReadReservations(directory))
            {
                // Resuming this exact source keeps its build identity. Other source commits
                // must advance beyond every attempt, including deliveries absent from Apple APIs.
                if ((reservation.Identity ?? reservation.Build.ToString(System.Globalization.CultureInfo.InvariantCulture)) == current.ToString(System.Globalization.CultureInfo.InvariantCulture) && !string.IsNullOrWhiteSpace(source) &&
                    string.Equals(reservation.Source, source, StringComparison.OrdinalIgnoreCase)) continue;
                highest = Math.Max(highest, reservation.Build);
            }
        }
        return highest;
    }

    internal void Reserve(PowerForgeAppleReleasePlan plan, string build, string? bundleId = null)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceCommit)) return;
        if (!Regex.IsMatch(build, @"^[0-9]+(?:\.[0-9]+){0,2}$") || !long.TryParse(build.Split('.')[0], out var number) || number <= 0)
            throw new InvalidOperationException("Apple delivery reservation needs a valid numeric Apple build identity.");
        foreach (var directory in _directories.Where(value => bundleId is null || Path.GetFileName(value) == Hash(bundleId.Trim().ToLowerInvariant())))
        {
            var reservations = ReadReservations(directory).ToList();
            var existing = reservations.SingleOrDefault(value => (value.Identity ?? value.Build.ToString(System.Globalization.CultureInfo.InvariantCulture)) == build);
            if (existing is not null)
            {
                if (string.IsNullOrWhiteSpace(plan.SourceCommit) || !string.Equals(existing.Source, plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Apple build {number} is already reserved for another source.");
                continue;
            }
            reservations.Add(new Reservation { Build = number, Identity = build, Source = plan.SourceCommit, ReservedAt = DateTimeOffset.UtcNow });
            AtomicWrite(Path.Combine(directory, "reservations.json"), reservations);
        }
    }

    internal static void AssertMutationAllowed(PowerForgeAppleReleasePlan plan, string? stateRoot = null, bool beforeMutation = false)
    {
        foreach (var directory in Directories(plan, stateRoot))
            if (File.Exists(Path.Combine(directory, "paused")))
                throw new AppleReleaseGateClosedException("Apple operations for this app are paused by the local operator.", beforeMutation);
        var gate = Environment.GetEnvironmentVariable("POWERFORGE_APPLE_WORKER_GATE");
        if (string.IsNullOrWhiteSpace(gate)) return;
        if (!GateAllowsMutation(gate, DateTimeOffset.UtcNow, out var reason))
            throw new AppleReleaseGateClosedException("TestFlight mutation gate closed: " + reason, beforeMutation);
    }

    internal static bool GateAllowsMutation(string gate, DateTimeOffset now)
        => GateAllowsMutation(gate, now, out _);

    internal static bool GateAllowsMutation(string gate, DateTimeOffset now, out string? reason)
    {
        AssertUnlinked(gate);
        using var document = JsonDocument.Parse(File.ReadAllText(gate));
        var value = document.RootElement;
        var expires = value.GetProperty("ExpiresAt").GetDateTimeOffset();
        var allowed = value.GetProperty("Allowed").GetBoolean() && expires > now;
        var observed = value.TryGetProperty("Reason", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null;
        reason = allowed ? null : expires <= now ? $"Request observation heartbeat expired at {expires:O}. {observed}" :
            observed ?? "Automatic mode, selection labels, or local pause holds the next mutation.";
        if (reason?.Length > 1200) reason = reason.Substring(0, 1200);
        return allowed;
    }

    internal static string AppDirectory(string bundleId) => Path.Combine(Root, "apps", Hash(bundleId.Trim().ToLowerInvariant()));

    internal static void AtomicWrite<T>(string path, T value)
        => AtomicWriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value));

    internal static void AtomicWriteBytes(string path, byte[] bytes)
    {
        AssertUnlinked(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void AssertUnlinked(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Apple local state must not traverse linked paths: {current}");
        }
    }

    internal static void EnsureDirectory(string path)
    {
        AssertUnlinked(path);
        Directory.CreateDirectory(path);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable("POWERFORGE_APPLE_STATE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathRooted(configured) || !Directory.Exists(configured))
                throw new InvalidOperationException("POWERFORGE_APPLE_STATE_ROOT must identify an existing absolute local directory on the mounted development volume.");
            AssertUnlinked(configured);
            return Path.GetFullPath(configured);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerForge", "Apple");
    }

    private static string[] Directories(PowerForgeAppleReleasePlan plan, string? stateRoot = null)
    {
        var apps = plan.Apps;
        if (apps.Any(app => PowerForgeReleaseService.ShouldExecuteAppleTarget(plan.Action, app) && string.IsNullOrWhiteSpace(app.BundleId)))
            throw new InvalidOperationException("Every mutating Apple target requires BundleId for its shared cross-checkout app lease.");
        return apps.Where(app => !string.IsNullOrWhiteSpace(app.BundleId)).Select(app => app.BundleId!.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal)
            .Select(value => Path.Combine(stateRoot ?? Root, "apps", Hash(value)))
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool IsLockSharingConflict(IOException exception)
        => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
            : exception.HResult == (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? 35 : 11);

    internal static string Hash(string value)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    private static Reservation[] ReadReservations(string directory)
    {
        var path = Path.Combine(directory, "reservations.json");
        AssertUnlinked(path);
        if (!File.Exists(path)) return Array.Empty<Reservation>();
        return JsonSerializer.Deserialize<Reservation[]>(File.ReadAllText(path)) ??
               throw new InvalidOperationException("Apple build reservation journal is empty or invalid.");
    }

    internal sealed class Reservation
    {
        // Integer allocation advances beyond the first component of a dotted manual build.
        public long Build { get; set; }
        public string? Identity { get; set; }
        public string? Source { get; set; }
        public DateTimeOffset ReservedAt { get; set; }
    }

    public void Dispose() { for (var index = _leases.Count - 1; index >= 0; index--) _leases[index].Dispose(); }
}

/// <summary>The canonical app lease could not be acquired; no release mutation has started.</summary>
internal sealed class AppleReleaseLeaseContentionException(Exception inner)
    : InvalidOperationException("Another checkout owns this app's Apple release lease. Retry after it finishes.", inner)
{
    internal const int ExitCode = 75;
}

/// <summary>A closed mutation gate. Only entry rejection proves that this invocation has not mutated.</summary>
internal sealed class AppleReleaseGateClosedException(string message, bool beforeMutation) : InvalidOperationException(message)
{
    internal bool BeforeMutation { get; } = beforeMutation;
}
