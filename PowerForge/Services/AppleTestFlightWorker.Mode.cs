#if NET8_0_OR_GREATER
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    /// <summary>Records deliberate local scheduling intent without building, uploading, or resetting enrollment.</summary>
    internal AppleTestFlightWorkerState SetAutomaticMode(AppleTestFlightAutomaticMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown automatic TestFlight mode.");
        AssertOwner();
        AppleReleaseAppState.EnsureDirectory(_directory);
        var path = Path.Combine(_directory, "worker.lock");
        AppleReleaseAppState.AssertUnlinked(path);
        using var lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _state = LoadState();
        ApplyRequest(_state, new AppleTestFlightRequestRecord
        {
            Key = "local:" + Guid.NewGuid().ToString("N"), Actor = Environment.UserName,
            Operation = mode switch
            {
                AppleTestFlightAutomaticMode.Off => "Automatic: Off",
                AppleTestFlightAutomaticMode.SelectedPRs => "Automatic: Selected PRs",
                _ => "Automatic: Every merged PR"
            },
            RecordedAt = DateTimeOffset.UtcNow
        });
        CloseGate();
        Save();
        return _state;
    }
}
#endif
