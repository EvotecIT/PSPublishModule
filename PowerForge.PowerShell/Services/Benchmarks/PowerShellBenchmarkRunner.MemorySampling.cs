namespace PowerForge;

public sealed partial class PowerShellBenchmarkRunner
{
    private static readonly string[] MemorySamplingMetricNames =
    {
        "MemorySampleCount", "WorkingSetSampleCount", "MemorySamplingIntervalMs", "MemorySamplingFailed",
        "BaselineManagedHeapBytes", "SampledMaxManagedHeapBytes", "SampledManagedHeapDeltaBytes",
        "BaselineWorkingSetBytes", "SampledMaxWorkingSetBytes", "SampledWorkingSetDeltaBytes"
    };

    // Observed maxima are lower bounds on transient peaks. Setup, validation and
    // sampler startup/teardown stay outside timing; observations perturb the host.
    private sealed class OperationMemorySampler : IDisposable
    {
        private readonly object _gate = new();
        private readonly System.Threading.ManualResetEvent _stop = new(false);
        private readonly System.Threading.Thread _thread;
        private readonly int _intervalMilliseconds;
        private readonly long _baselineManagedHeap;
        private readonly long? _baselineWorkingSet;
        private long _maximumManagedHeap;
        private long? _maximumWorkingSet;
        private int _sampleCount;
        private int _workingSetSampleCount;
        private bool _stopped;
        private Exception? _failure;

        internal OperationMemorySampler(int intervalMilliseconds)
        {
            _intervalMilliseconds = intervalMilliseconds;
            _baselineManagedHeap = GC.GetTotalMemory(forceFullCollection: false);
            _baselineWorkingSet = ReadWorkingSet();
            _maximumManagedHeap = _baselineManagedHeap;
            _maximumWorkingSet = _baselineWorkingSet;
            _sampleCount = 1;
            _workingSetSampleCount = _baselineWorkingSet.HasValue ? 1 : 0;
            _thread = new System.Threading.Thread(Sample)
            {
                IsBackground = true,
                Name = "PowerForge benchmark memory sampler"
            };
            try { _thread.Start(); }
            catch { _stop.Dispose(); throw; }
        }

        private void Sample()
        {
            try
            {
                while (!_stop.WaitOne(_intervalMilliseconds))
                {
                    lock (_gate)
                    {
                        if (_stopped) return;
                        Observe();
                    }
                }
            }
            catch (Exception exception)
            {
                lock (_gate) _failure = exception;
            }
        }

        private void Observe()
        {
            _maximumManagedHeap = Math.Max(_maximumManagedHeap, GC.GetTotalMemory(forceFullCollection: false));
            long? workingSet = ReadWorkingSet();
            if (workingSet.HasValue)
            {
                _maximumWorkingSet = Math.Max(_maximumWorkingSet ?? workingSet.Value, workingSet.Value);
                _workingSetSampleCount++;
            }
            _sampleCount++;
        }

        internal void Stop()
        {
            lock (_gate)
            {
                if (_stopped) return;
                try { Observe(); }
                catch (Exception exception) { _failure ??= exception; }
                _stopped = true;
            }
            _stop.Set();
            _thread.Join();
            _stop.Dispose();
        }

        internal void ThrowIfFailed()
        {
            if (_failure is not null)
                throw new InvalidOperationException("Operation memory sampling failed.", _failure);
        }

        internal Dictionary<string, double> AddMetrics(Dictionary<string, double>? metrics)
        {
            Stop();
            metrics ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            metrics["MemorySampleCount"] = _sampleCount;
            metrics["WorkingSetSampleCount"] = _workingSetSampleCount;
            metrics["MemorySamplingIntervalMs"] = _intervalMilliseconds;
            metrics["MemorySamplingFailed"] = _failure is null ? 0 : 1;
            metrics["BaselineManagedHeapBytes"] = _baselineManagedHeap;
            metrics["SampledMaxManagedHeapBytes"] = _maximumManagedHeap;
            metrics["SampledManagedHeapDeltaBytes"] = _maximumManagedHeap - _baselineManagedHeap;
            if (_maximumWorkingSet.HasValue) metrics["SampledMaxWorkingSetBytes"] = _maximumWorkingSet.Value;
            if (_baselineWorkingSet.HasValue)
            {
                metrics["BaselineWorkingSetBytes"] = _baselineWorkingSet.Value;
                if (_maximumWorkingSet.HasValue)
                    metrics["SampledWorkingSetDeltaBytes"] = _maximumWorkingSet.Value - _baselineWorkingSet.Value;
            }
            return metrics;
        }

        public void Dispose() => Stop();
    }
}
