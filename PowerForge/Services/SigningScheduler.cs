using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace PowerForge;

/// <summary>
/// Runs per-file signing work so a hardware token is unlocked once and the remaining
/// files are signed and timestamped concurrently.
/// </summary>
/// <remarks>
/// Items run one at a time on the calling thread until one item actually uses the signing
/// key (<see cref="SigningItemOutcome.Signed"/>), so any PIN or consent prompt happens once
/// before other work starts. Items that were skipped (for example already signed) do not
/// count as that unlock. Remaining items then run on up to <c>parallelism</c> worker threads.
/// A parallelism of 1 keeps the whole run serial on the calling thread. The first failure
/// stops new items from starting; items already in flight finish and are reported. Start and
/// finish callbacks always run on the calling thread, so loggers and host UI that are not
/// thread-safe can be used from them.
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
    /// <param name="parallelism">Maximum concurrent items once the key has been used.</param>
    /// <param name="sign">Signs one item and reports what happened; throwing counts as <see cref="SigningItemOutcome.Failed"/>. Runs on the calling thread until the key is unlocked and in serial mode, otherwise on a worker thread.</param>
    /// <param name="started">Called on the calling thread when an item starts.</param>
    /// <param name="finished">Called on the calling thread when an item finishes, with its error when it threw.</param>
    /// <param name="cancellationToken">Stops new items from starting.</param>
    internal static SigningScheduleResult Run(
        int count,
        int parallelism,
        Func<int, SigningItemOutcome> sign,
        Action<int>? started = null,
        Action<int, SigningItemOutcome, Exception?>? finished = null,
        CancellationToken cancellationToken = default)
    {
        if (sign is null) throw new ArgumentNullException(nameof(sign));
        var result = new SigningScheduleResult(count);
        var next = 0;

        // Serial until one item has actually used the private key (or for the whole run).
        var unlocked = false;
        while (next < count && (!unlocked || parallelism <= 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = RunInline(next++, sign, started, finished, result);
            if (outcome == SigningItemOutcome.Failed)
                return result;
            unlocked |= outcome == SigningItemOutcome.Signed;
        }

        if (next < count)
            RunParallel(next, count, Math.Min(parallelism, count - next), sign, started, finished, result, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static SigningItemOutcome RunInline(
        int index,
        Func<int, SigningItemOutcome> sign,
        Action<int>? started,
        Action<int, SigningItemOutcome, Exception?>? finished,
        SigningScheduleResult result)
    {
        result.Attempted[index] = true;
        started?.Invoke(index);
        var (outcome, error) = Invoke(sign, index);
        if (outcome == SigningItemOutcome.Failed)
            result.RecordFailure(index, error);
        finished?.Invoke(index, outcome, error);
        return outcome;
    }

    private static (SigningItemOutcome Outcome, Exception? Error) Invoke(Func<int, SigningItemOutcome> sign, int index)
    {
        try
        {
            return (sign(index), null);
        }
        catch (Exception ex)
        {
            return (SigningItemOutcome.Failed, ex);
        }
    }

    private static void RunParallel(
        int first,
        int count,
        int workers,
        Func<int, SigningItemOutcome> sign,
        Action<int>? started,
        Action<int, SigningItemOutcome, Exception?>? finished,
        SigningScheduleResult result,
        CancellationToken cancellationToken)
    {
        using var events = new BlockingCollection<Action>();
        var next = first;
        var tasks = new Task[workers];
        for (var worker = 0; worker < workers; worker++)
        {
            tasks[worker] = Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    // Claiming and stopping share one lock, so no item is claimed after a failure.
                    if (cancellationToken.IsCancellationRequested || !result.TryClaim(ref next, count, out var index))
                        return;

                    events.Add(() => started?.Invoke(index));
                    var (outcome, error) = Invoke(sign, index);
                    if (outcome == SigningItemOutcome.Failed)
                        result.RecordFailure(index, error);
                    events.Add(() => finished?.Invoke(index, outcome, error));
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        var completion = Task.Factory.ContinueWhenAll(tasks, _ => events.CompleteAdding(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        ExceptionDispatchInfo? reportFailure = null;
        foreach (var report in events.GetConsumingEnumerable())
        {
            if (reportFailure is not null)
                continue;
            try
            {
                report();
            }
            catch (Exception ex)
            {
                // Stop new claims but let in-flight signing finish before rethrowing.
                reportFailure = ExceptionDispatchInfo.Capture(ex);
                result.StopClaims();
            }
        }

        Task.WaitAll(tasks);
        completion.Wait();
        reportFailure?.Throw();
    }
}

/// <summary>What happened to one scheduled signing item.</summary>
internal enum SigningItemOutcome
{
    /// <summary>The item was signed with the private key.</summary>
    Signed,

    /// <summary>The item needed no signing (for example it was already signed); the key was not used.</summary>
    Skipped,

    /// <summary>Signing failed; no further items start.</summary>
    Failed
}

/// <summary>
/// Outcome of a <see cref="SigningScheduler"/> run.
/// </summary>
internal sealed class SigningScheduleResult
{
    private readonly object _gate = new();
    private readonly List<SigningScheduleFailure> _failures = new();
    private bool _stopped;

    internal SigningScheduleResult(int count) => Attempted = new bool[Math.Max(count, 0)];

    /// <summary>Items that were started, by index.</summary>
    internal bool[] Attempted { get; }

    /// <summary>Failures in the order they were recorded; the first one stopped the run.</summary>
    internal IReadOnlyList<SigningScheduleFailure> Failures
    {
        get { lock (_gate) return _failures.ToArray(); }
    }

    /// <summary>True when every item was started and none failed.</summary>
    internal bool Succeeded
    {
        get { lock (_gate) return _failures.Count == 0; }
    }

    internal bool TryClaim(ref int next, int count, out int index)
    {
        lock (_gate)
        {
            index = next;
            if (_stopped || index >= count)
                return false;
            next++;
            Attempted[index] = true;
            return true;
        }
    }

    internal void RecordFailure(int index, Exception? error)
    {
        lock (_gate)
        {
            _stopped = true;
            _failures.Add(new SigningScheduleFailure(index, error));
        }
    }

    internal void StopClaims()
    {
        lock (_gate) _stopped = true;
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
