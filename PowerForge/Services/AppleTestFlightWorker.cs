#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>One bounded local queue pass; scheduling remains an explicit operator action.</summary>
internal sealed partial class AppleTestFlightWorker
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly AppleTestFlightWorkerPolicy _policy;
    private readonly string _directory;
    private readonly string _journal;
    private readonly string _gate;
    private AppleTestFlightWorkerState _state = new();
    private AppleTestFlightJob? _running;
    private AppleTestFlightJobStage? _releasingFrom;

    internal AppleTestFlightWorker(AppleTestFlightWorkerPolicy policy)
    {
        _policy = policy;
        ValidatePolicy(policy);
        _directory = Path.Combine(policy.StateRoot, "queues", AppleReleaseAppState.Hash(policy.Repository.ToLowerInvariant()));
        _journal = Path.Combine(_directory, "queue.json");
        _gate = Path.Combine(_directory, "gate.json");
    }

    internal AppleTestFlightWorkerState Run(bool inspectOnly = false, string? retireReconciledJob = null)
    {
        AppleReleaseAppState.EnsureDirectory(_directory);
        // queue.json is atomically replaced. Inspection and pause acknowledgement
        // must remain available while the writer holds its process lease.
        if (inspectOnly && retireReconciledJob is null) return LoadState();
        var lockPath = Path.Combine(_directory, "worker.lock");
        AppleReleaseAppState.AssertUnlinked(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _state = LoadState();
        if (retireReconciledJob is not null)
        {
            var held = _state.Jobs.SingleOrDefault(value => value.Id == retireReconciledJob && value.Stage == AppleTestFlightJobStage.NeedsOperator)
                       ?? throw new InvalidOperationException("Only an existing NeedsOperator job may be retired after local reconciliation.");
            held.Stage = AppleTestFlightJobStage.Excluded;
            held.Diagnostic = "Local operator confirmed remote delivery and canonical receipt reconciliation; held intent retired.";
            held.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            PublishStatus(held);
        }
        if (inspectOnly) return _state;
        AssertOwner();
        SyncRequests();
        foreach (var pendingStatus in _state.Jobs.Where(value => value.StatusPending).ToArray()) PublishStatus(pendingStatus);
        ReconcileAutomaticSelection();
        EnqueueMergedRequests();
        _running = SelectRunnableJob(_state);
        if (_state.Jobs.Any(job => job.Stage == AppleTestFlightJobStage.NeedsOperator))
        {
            AppleReleaseAppState.AtomicWrite(_gate, new { Allowed = false, ExpiresAt = DateTimeOffset.UtcNow });
            Save();
            return _state;
        }
        if (_running is null) { CloseGate(); Save(); return _state; }
        try
        {
            while (!UpdateGate())
            {
                SetStage(_running.Stage, "Automatic work is Off, excluded, or locally paused; existing recovery stage is preserved.");
                if (!_running.Automatic || !IsUnstarted(_running.Stage)) return _state;
                // An untouched automatic intent must not starve a later explicit
                // request. A checkpoint or delivery recovery still owns ordering.
                _running = SelectManualBehindHeldIntent(_state);
                if (_running is null) return _state;
            }
            Advance(_running);
        }
        catch (Exception exception)
        {
            SetStage(FailureStage(_running!.Stage, exception, _releasingFrom), SafeDiagnostic(exception.Message));
        }
        finally
        {
            CloseGate();
            Save();
            if (_running is not null) PublishStatus(_running);
        }
        return _state;
    }

    internal static AppleTestFlightJob? SelectRunnableJob(AppleTestFlightWorkerState state) =>
        state.Jobs.Any(job => job.Stage == AppleTestFlightJobStage.NeedsOperator) ? null :
        state.Jobs.FirstOrDefault(job => job.Stage is not (AppleTestFlightJobStage.ReadyInHome or AppleTestFlightJobStage.Excluded) &&
            !(job.Automatic && state.Mode == AppleTestFlightAutomaticMode.Off && IsUnstarted(job.Stage)));

    internal static bool IsUnstarted(AppleTestFlightJobStage stage) => stage is
        AppleTestFlightJobStage.Queued or AppleTestFlightJobStage.WaitingForChecks or AppleTestFlightJobStage.Planning;

    internal static AppleTestFlightJob? SelectManualBehindHeldIntent(AppleTestFlightWorkerState state) =>
        state.Jobs.FirstOrDefault(job => !job.Automatic && job.Stage is not
            (AppleTestFlightJobStage.ReadyInHome or AppleTestFlightJobStage.Excluded));

    internal static AppleTestFlightJobStage FailureStage(AppleTestFlightJobStage stage, Exception exception, AppleTestFlightJobStage? releasingFrom = null) =>
        exception is AppleTestFlightPreMutationRejectedException
            ? stage switch
            {
                AppleTestFlightJobStage.CheckpointExecuting => AppleTestFlightJobStage.Planning,
                AppleTestFlightJobStage.Releasing => releasingFrom == AppleTestFlightJobStage.AppleProcessingPending
                    ? AppleTestFlightJobStage.AppleProcessingPending : AppleTestFlightJobStage.ReleaseReady,
                _ => AppleTestFlightJobStage.NeedsOperator
            }
            : exception is AppleTestFlightToolException && stage is not (AppleTestFlightJobStage.CheckpointExecuting or
            AppleTestFlightJobStage.PublishingCheckpoint or AppleTestFlightJobStage.Releasing)
            ? stage : AppleTestFlightJobStage.NeedsOperator;

    private AppleTestFlightWorkerState LoadState()
    {
        AppleReleaseAppState.AssertUnlinked(_journal);
        var state = File.Exists(_journal)
            ? JsonSerializer.Deserialize<AppleTestFlightWorkerState>(File.ReadAllText(_journal), Json) ?? throw new InvalidOperationException("Worker journal is invalid.")
            : new AppleTestFlightWorkerState { Repository = _policy.Repository };
        if (state.SchemaVersion != 1 || state.Repository != _policy.Repository)
            throw new InvalidOperationException("Worker journal schema or repository does not match policy.");
        state.Paused = _policy.Targets.Any(target => File.Exists(Path.Combine(AppleReleaseAppState.AppDirectory(target.BundleId), "paused")));
        return state;
    }

    private void CloseGate() => AppleReleaseAppState.AtomicWrite(_gate, new { Allowed = false, ExpiresAt = DateTimeOffset.UtcNow });

    internal void SetPaused(bool paused)
    {
        foreach (var target in _policy.Targets)
        {
            var directory = AppleReleaseAppState.AppDirectory(target.BundleId);
            AppleReleaseAppState.EnsureDirectory(directory);
            var path = Path.Combine(directory, "paused");
            AppleReleaseAppState.AssertUnlinked(path);
            if (paused) AppleReleaseAppState.AtomicWrite(path, new { PausedAt = DateTimeOffset.UtcNow });
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static void ValidatePolicy(AppleTestFlightWorkerPolicy policy)
    {
        if (!Regex.IsMatch(policy.Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ||
            !Regex.IsMatch(policy.OwnerCommit, @"^[0-9a-fA-F]{40}$") || !Regex.IsMatch(policy.BaselineSourceCommit, @"^[0-9a-fA-F]{40}$") ||
            policy.AcceptRequestsAfter == default || policy.AcceptRequestsAfter > DateTimeOffset.UtcNow)
            throw new ArgumentException("Worker needs an exact owner pin, repository, and explicit request enrollment time.");
        policy.OwnerCommit = policy.OwnerCommit.ToLowerInvariant();
        policy.BaselineSourceCommit = policy.BaselineSourceCommit.ToLowerInvariant();
        foreach (var root in new[] { policy.ConsumerRoot, policy.OwnerRoot, policy.StateRoot, policy.ScratchRoot })
        {
            if (!Path.IsPathRooted(root) || !Directory.Exists(root))
                throw new ArgumentException("Every worker storage root must exist at an absolute path on its mounted volume.");
            AppleReleaseAppState.AssertUnlinked(root);
        }
        if (Path.GetFullPath(policy.StateRoot) != Path.GetFullPath(AppleReleaseAppState.Root))
            throw new ArgumentException("Worker StateRoot must equal POWERFORGE_APPLE_STATE_ROOT used by manual release entrypoints.");
        if (policy.RequiredChecks.Length == 0 || policy.RequiredChecks.Any(string.IsNullOrWhiteSpace) ||
            policy.Targets.Length == 0 || policy.Targets.Select(value => value.Name).Distinct().Count() != policy.Targets.Length ||
            policy.Targets.Any(value => string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.BundleId) || string.IsNullOrWhiteSpace(value.AppId)) ||
            !Guid.TryParse(policy.BetaGroupId, out _) || string.IsNullOrWhiteSpace(policy.BetaGroupName))
            throw new ArgumentException("Worker policy must name exact checks, app targets, and one existing internal beta group.");
        foreach (var path in policy.PublisherPaths.Append(policy.ConfigPath).Append(policy.WorkflowPath).Append(policy.QualificationWorkflowPath))
            ContainedPath(policy.ConsumerRoot, path);
        if (policy.PublisherPaths.Length == 0) throw new ArgumentException("Publisher checkpoint paths must be explicit.");
        if (IsWithin(policy.ConsumerRoot, policy.StateRoot) || IsWithin(policy.OwnerRoot, policy.StateRoot))
            throw new ArgumentException("Durable worker state must remain outside source checkouts.");
    }

    private void Save()
    {
        _state.HeartbeatAt = DateTimeOffset.UtcNow;
        AppleReleaseAppState.AtomicWrite(_journal, _state);
    }

    private void SetStage(AppleTestFlightJobStage stage, string? diagnostic = null)
    {
        _running!.Stage = stage;
        _running.Diagnostic = diagnostic;
        _running.UpdatedAt = DateTimeOffset.UtcNow;
        Save();
        PublishStatus(_running);
    }

    private bool UpdateGate()
    {
        var allowed = !File.Exists(Path.Combine(_directory, "paused")) &&
                      (_running is null || !_running.Automatic || _state.Mode != AppleTestFlightAutomaticMode.Off);
        if (allowed && _running?.Automatic == true)
        {
            foreach (var number in _state.Requests.Where(value => _running.RequestKeys.Contains(value.Key) && value.PullRequest > 0).Select(value => value.PullRequest).Distinct())
            {
                var pull = Api($"pulls/{number}");
                var labels = pull.GetProperty("labels").EnumerateArray().Select(value => Text(value, "name")).ToArray();
                if (labels.Contains("testflight-skip") || (_state.Mode == AppleTestFlightAutomaticMode.SelectedPRs && !labels.Contains("testflight"))) allowed = false;
            }
        }
        foreach (var target in _policy.Targets)
            if (File.Exists(Path.Combine(AppleReleaseAppState.AppDirectory(target.BundleId), "paused"))) allowed = false;
        AppleReleaseAppState.AtomicWrite(_gate, new { Allowed = allowed, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3) });
        return allowed;
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal));
    }

    private static string ContainedPath(string root, string path)
    {
        if (Path.IsPathRooted(path) || !IsWithin(root, Path.Combine(root, path)))
            throw new ArgumentException("Worker paths must be relative to the trusted consumer checkout.");
        var full = Path.GetFullPath(Path.Combine(root, path));
        AppleReleaseAppState.AssertUnlinked(full);
        return full;
    }

    private static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : "";

    private static string SafeDiagnostic(string value)
    {
        var safe = Regex.Replace(value, @"(?i)(?:[^\s""']*\.appstoreconnect[/\\][^\s""']*)", "[private profile]");
        return safe.Length <= 1200 ? safe : safe.Substring(0, 1200);
    }
}
#endif
