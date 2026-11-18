using System.Diagnostics;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Says that a long operation is still running, so a slow external process, download or upload never looks hung.
/// Every <see cref="DefaultInterval"/> it logs "&lt;label&gt; is still running (elapsed)" at Info and, when given,
/// sends the same text to a progress detail. Dispose it when the operation finishes.
/// </summary>
/// <remarks>
/// The timer fires on a thread-pool thread, so the logger and detail sink must accept calls from any thread
/// (every <see cref="ILogger"/> in this solution does).
/// </remarks>
internal sealed class OperationHeartbeat : IDisposable
{
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;
    private readonly string _label;
    private readonly Action<string>? _detail;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Timer _timer;
    private int _disposed;

    private OperationHeartbeat(ILogger logger, string label, Action<string>? detail, TimeSpan interval)
    {
        _logger = logger;
        _label = label;
        _detail = detail;
        _timer = new Timer(_ => Beat(), null, interval, interval);
    }

    /// <summary>Starts reporting for <paramref name="label"/> (for example "dotnet publish PowerForge (net8.0)").</summary>
    public static OperationHeartbeat Start(ILogger logger, string label, Action<string>? detail = null, TimeSpan? interval = null)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A label is required.", nameof(label));
        var period = interval ?? DefaultInterval;
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        return new OperationHeartbeat(logger, label, detail, period);
    }

    /// <summary>Time since the heartbeat started.</summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>The message a beat writes now.</summary>
    internal string Message => $"{_label} is still running ({DotNetRepositoryReleaseService.FormatDuration(_stopwatch.Elapsed)})";

    internal void Beat()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var message = Message;
        try { _logger.Info(message); } catch { /* reporting must never fail the operation */ }
        try { _detail?.Invoke(message); } catch { /* progress is best effort */ }
    }

    /// <summary>Stops reporting.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        _stopwatch.Stop();
    }
}
