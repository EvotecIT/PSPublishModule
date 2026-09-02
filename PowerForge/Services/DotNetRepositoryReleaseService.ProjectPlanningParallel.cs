using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private T[] ResolveProjectPlanningItems<T>(
        int itemCount,
        Func<int, ILogger, T> resolver,
        Action<int, T, int> itemCompleted,
        CancellationToken cancellationToken)
    {
        var results = new T[itemCount];
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
        }

        worker.GetAwaiter().GetResult();
        if (workerFailure is not null)
            ExceptionDispatchInfo.Capture(workerFailure).Throw();

        return results;
    }

    private void ReplayBufferedLogs(BufferedLogger? bufferedLogger)
    {
        if (bufferedLogger is null)
            return;

        foreach (var entry in bufferedLogger.Entries)
        {
            switch (entry.Level)
            {
                case "success":
                    _logger.Success(entry.Message);
                    break;
                case "warn":
                    _logger.Warn(entry.Message);
                    break;
                case "error":
                    _logger.Error(entry.Message);
                    break;
                case "verbose":
                    _logger.Verbose(entry.Message);
                    break;
                default:
                    _logger.Info(entry.Message);
                    break;
            }
        }
    }
}
