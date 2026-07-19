namespace PowerForge;

public sealed partial class PowerShellBenchmarkRunner
{
    private List<PowerShellBenchmarkWorkItem> WarmUpWorkItems(
        PowerShellBenchmarkSuite suite,
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        string runId,
        ICollection<BenchmarkSample> samples)
    {
        var runnable = new List<PowerShellBenchmarkWorkItem>(items.Count);
        foreach (var item in items)
        {
            var warmupFailed = false;
            for (var warmup = 0; warmup < Math.Max(0, suite.WarmupCount); warmup++)
            {
                var warmupSample = InvokeMeasuredIteration(
                    suite,
                    item,
                    ToPsObject(item.Values),
                    -warmup - 1,
                    runId,
                    recordSample: false);
                if (warmupSample.Status != BenchmarkSampleStatus.Failed)
                    continue;

                samples.Add(warmupSample);
                warmupFailed = true;
                break;
            }

            if (!warmupFailed)
                runnable.Add(item);
        }

        return runnable;
    }

    private void RunInterleavedMeasurements(
        PowerShellBenchmarkSuite suite,
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        string runId,
        ICollection<BenchmarkSample> samples)
    {
        for (var iteration = 0; iteration < Math.Max(1, suite.IterationCount); iteration++)
        {
            foreach (var item in OrderWorkItems(items, iteration, suite.RunOrder))
            {
                samples.Add(InvokeMeasuredIteration(
                    suite,
                    item,
                    ToPsObject(item.Values),
                    iteration,
                    runId,
                    recordSample: true));
                ApplyCooldown(suite);
            }
        }
    }

    private void RunGroupedMeasurements(
        PowerShellBenchmarkSuite suite,
        IReadOnlyList<PowerShellBenchmarkWorkItem> items,
        string runId,
        ICollection<BenchmarkSample> samples)
    {
        foreach (var group in GroupComparisonWorkItems(items))
        {
            var runnable = WarmUpWorkItems(suite, group, runId, samples);
            for (var iteration = 0; iteration < Math.Max(1, suite.IterationCount); iteration++)
            {
                foreach (var item in Rotate(runnable, iteration))
                {
                    samples.Add(InvokeMeasuredIteration(
                        suite,
                        item,
                        ToPsObject(item.Values),
                        iteration,
                        runId,
                        recordSample: true));
                    ApplyCooldown(suite);
                }
            }
        }
    }
}
