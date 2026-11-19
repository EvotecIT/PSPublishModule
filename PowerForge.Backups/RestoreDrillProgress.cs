namespace PowerForge.Backups;

/// <summary>What a step's progress counts.</summary>
public enum RestoreDrillUnit
{
    /// <summary>The step reports no amount; only its running time shows it is working.</summary>
    None,
    /// <summary>Bytes read or copied.</summary>
    Bytes,
    /// <summary>A percentage reported by SQL Server.</summary>
    Percent
}

/// <summary>
/// Receives a restore drill's steps as they run: every step is planned before the first one starts, so a view can list
/// the whole drill up front. Calls arrive from the drill and from SQL Server progress polling, so implementations must be
/// thread-safe and return quickly.
/// </summary>
public interface IRestoreDrillProgress
{
    /// <summary>Lists a step. <paramref name="total"/> is the amount it will report, or 0 when it reports none.</summary>
    void Plan(string key, string label, long total, RestoreDrillUnit unit);

    /// <summary>Marks a step as running.</summary>
    void Start(string key, string? detail = null);

    /// <summary>Reports how much of a running step is done, in its unit.</summary>
    void Report(string key, long done, string? detail = null);

    /// <summary>Ends a step.</summary>
    void Finish(string key, bool succeeded, string? detail = null);
}

internal sealed class NoRestoreDrillProgress : IRestoreDrillProgress
{
    internal static readonly NoRestoreDrillProgress Instance = new();
    public void Plan(string key, string label, long total, RestoreDrillUnit unit) { }
    public void Start(string key, string? detail = null) { }
    public void Report(string key, long done, string? detail = null) { }
    public void Finish(string key, bool succeeded, string? detail = null) { }
}

/// <summary>Remembers the running step so a failure or cancellation can end it with the reason.</summary>
internal sealed class RestoreDrillSteps(IRestoreDrillProgress progress)
{
    private string? _running;

    internal IRestoreDrillProgress Progress { get; } = progress;

    internal void Start(string key, string? detail = null)
    {
        _running = key;
        Progress.Start(key, detail);
    }

    internal void Finish(string key, string? detail = null)
    {
        if (_running == key) _running = null;
        Progress.Finish(key, true, detail);
    }

    internal void FailRunning(string reason)
    {
        if (_running is null) return;
        Progress.Finish(_running, false, reason);
        _running = null;
    }
}
