using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    // Evaluation logs are buffered per project until it finishes, so the phase reports its own count while it runs.
    internal static TimeSpan PlanningProgressInterval { get; set; } = OperationHeartbeat.DefaultInterval;

    private T[] ResolveProjectPlanningItems<T>(
        string label,
        int itemCount,
        Func<int, ILogger, T> resolver,
        Action<int, T, int> itemCompleted,
        CancellationToken cancellationToken)
    {
        var results = new T[itemCount];
        var watch = Stopwatch.StartNew();
        var finished = 0;
        var reporting = 1;
        _logger.Info($"{label} for {itemCount} project(s)...");
        using var progressTimer = new Timer(
            _ =>
            {
                if (Volatile.Read(ref reporting) == 0) return;
                try { _logger.Info($"{label}: {Volatile.Read(ref finished)}/{itemCount} done ({FormatDuration(watch.Elapsed)})"); }
                catch { /* reporting must never fail planning */ }
            },
            null,
            PlanningProgressInterval,
            PlanningProgressInterval);
        var bufferedLoggers = new BufferedLogger?[itemCount];
        using var completionQueue = new BlockingCollection<int>();
        Exception? workerFailure = null;
        var captureVerbose = _logger.IsVerbose;
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = GetProjectPlanningMaxDegree(itemCount)
        };

        var worker = Task.Run(() =>
        {
            try
            {
                Parallel.For(0, itemCount, options, index =>
                {
                    var bufferedLogger = new BufferedLogger { IsVerbose = captureVerbose };
                    bufferedLoggers[index] = bufferedLogger;
                    results[index] = resolver(index, bufferedLogger);
                    completionQueue.Add(index);
                });
            }
            catch (Exception ex)
            {
                workerFailure = ex;
            }
            finally
            {
                completionQueue.CompleteAdding();
            }
        });

        var completed = 0;
        foreach (var index in completionQueue.GetConsumingEnumerable())
        {
            ReplayBufferedLogs(bufferedLoggers[index]);
            itemCompleted(index, results[index], ++completed);
            Volatile.Write(ref finished, completed);
        }

        Volatile.Write(ref reporting, 0);
        worker.GetAwaiter().GetResult();
        if (workerFailure is not null)
            ExceptionDispatchInfo.Capture(workerFailure).Throw();

        _logger.Info($"{label}: {itemCount}/{itemCount} done in {FormatDuration(watch.Elapsed)}");
        return results;
    }

    private void ReplayBufferedLogs(BufferedLogger? bufferedLogger) => ReplayBufferedLogs(_logger, bufferedLogger);

    private static void ReplayBufferedLogs(ILogger target, BufferedLogger? bufferedLogger)
    {
        if (bufferedLogger is null)
            return;

        foreach (var entry in bufferedLogger.Entries)
        {
            switch (entry.Level)
            {
                case "success":
                    target.Success(entry.Message);
                    break;
                case "warn":
                    target.Warn(entry.Message);
                    break;
                case "error":
                    target.Error(entry.Message);
                    break;
                case "verbose":
                    target.Verbose(entry.Message);
                    break;
                default:
                    target.Info(entry.Message);
                    break;
            }
        }
    }
}
