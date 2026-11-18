using System.Collections.Concurrent;

namespace PowerForge;

/// <summary>
/// Runs per-file signing work so a hardware token is unlocked once and the remaining
/// files are signed and timestamped concurrently.
/// </summary>
/// <remarks>
/// The first item always runs alone on the calling thread, so any PIN or consent prompt
/// happens once before other work starts. Remaining items run on up to
/// <c>parallelism</c> worker threads. A parallelism of 1 keeps the whole run serial on the
/// calling thread. The first failure stops new items from starting; items already in
/// flight finish and are reported. Start and finish callbacks always run on the calling
/// thread, so loggers and host UI that are not thread-safe can be used from them.
/// </remarks>
internal static class SigningScheduler
{
    /// <summary>Parallelism used when neither the request nor the environment sets one.</summary>
    internal const int DefaultParallelism = 6;

    /// <summary>Upper bound for configured parallelism.</summary>
    internal const int MaxParallelism = 32;

    /// <summary>Environment variable that sets the default signing parallelism.</summary>
    internal const string ParallelismEnvironmentVariable = "POWERFORGE_SIGNING_PARALLELISM";

    /// <summary>
    /// Resolves the effective parallelism: an explicit positive request wins, then
    /// <see cref="ParallelismEnvironmentVariable"/>, then <see cref="DefaultParallelism"/>.
    /// </summary>
    internal static int ResolveParallelism(int requested)
    {
        if (requested > 0)
            return Math.Min(requested, MaxParallelism);

        var configured = Environment.GetEnvironmentVariable(ParallelismEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            value > 0)
            return Math.Min(value, MaxParallelism);

        return DefaultParallelism;
    }

    /// <summary>
    /// Signs <paramref name="count"/> items.
    /// </summary>
    /// <param name="count">Number of items.</param>
    /// <param name="parallelism">Maximum concurrent items after the first one.</param>
    /// <param name="sign">Signs one item; returns false or throws on failure. Runs on the calling thread for the first item and in serial mode, otherwise on a worker thread.</param>
    /// <param name="started">Called on the calling thread when an item starts.</param>
    /// <param name="finished">Called on the calling thread when an item finishes, with its error when it threw.</param>
    /// <param name="cancellationToken">Stops new items from starting.</param>
    internal static SigningScheduleResult Run(
        int count,
        int parallelism,
        Func<int, bool> sign,
        Action<int>? started = null,
        Action<int, bool, Exception?>? finished = null,
        CancellationToken cancellationToken = default)
    {
        if (sign is null) throw new ArgumentNullException(nameof(sign));
        var result = new SigningScheduleResult(count);
        if (count <= 0)
            return result;

        // The first item unlocks the private key; nothing else starts until it succeeds.
        if (!RunInline(0, sign, started, finished, result))
            return result;

        if (parallelism <= 1 || count == 1)
        {
            for (var index = 1; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!RunInline(index, sign, started, finished, result))
                    break;
            }
            return result;
        }

        RunParallel(count, Math.Min(parallelism, count - 1), sign, started, finished, result, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static bool RunInline(
        int index,
        Func<int, bool> sign,
        Action<int>? started,
        Action<int, bool, Exception?>? finished,
        SigningScheduleResult result)
    {
        result.Attempted[index] = true;
        started?.Invoke(index);
        bool succeeded;
        Exception? error = null;
        try
        {
            succeeded = sign(index);
        }
        catch (Exception ex)
        {
            succeeded = false;
            error = ex;
        }

        if (!succeeded)
            result.AddFailure(index, error);
        finished?.Invoke(index, succeeded, error);
        return succeeded;
    }

    private static void RunParallel(
        int count,
        int workers,
        Func<int, bool> sign,
        Action<int>? started,
        Action<int, bool, Exception?>? finished,
        SigningScheduleResult result,
        CancellationToken cancellationToken)
    {
        using var events = new BlockingCollection<Action>();
        var next = 0; // index 0 already ran inline
        var stopped = 0;
        var tasks = new Task[workers];
        for (var worker = 0; worker < workers; worker++)
        {
            tasks[worker] = Task.Factory.StartNew(() =>
            {
                while (Volatile.Read(ref stopped) == 0 && !cancellationToken.IsCancellationRequested)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= count)
                        return;

                    result.Attempted[index] = true;
                    events.Add(() => started?.Invoke(index));
                    bool succeeded;
                    Exception? error = null;
                    try
                    {
                        succeeded = sign(index);
                    }
                    catch (Exception ex)
                    {
                        succeeded = false;
                        error = ex;
                    }

                    if (!succeeded)
                    {
                        Interlocked.Exchange(ref stopped, 1);
                        result.AddFailure(index, error);
                    }
                    events.Add(() => finished?.Invoke(index, succeeded, error));
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        _ = Task.Factory.ContinueWhenAll(tasks, _ => events.CompleteAdding(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        foreach (var report in events.GetConsumingEnumerable())
            report();
        Task.WaitAll(tasks);
    }
}

/// <summary>
/// Outcome of a <see cref="SigningScheduler"/> run.
/// </summary>
internal sealed class SigningScheduleResult
{
    private readonly object _gate = new();
    private readonly List<SigningScheduleFailure> _failures = new();

    internal SigningScheduleResult(int count) => Attempted = new bool[Math.Max(count, 0)];

    /// <summary>Items that were started, by index.</summary>
    internal bool[] Attempted { get; }

    /// <summary>Failures in the order they happened; the first one stopped the run.</summary>
    internal IReadOnlyList<SigningScheduleFailure> Failures
    {
        get { lock (_gate) return _failures.ToArray(); }
    }

    /// <summary>True when every item was started and succeeded.</summary>
    internal bool Succeeded
    {
        get { lock (_gate) return _failures.Count == 0; }
    }

    internal void AddFailure(int index, Exception? error)
    {
        lock (_gate) _failures.Add(new SigningScheduleFailure(index, error));
    }
}

/// <summary>A failed item and the exception it threw, if any.</summary>
internal readonly struct SigningScheduleFailure
{
    internal SigningScheduleFailure(int index, Exception? error)
    {
        Index = index;
        Error = error;
    }

    internal int Index { get; }

    internal Exception? Error { get; }
}
