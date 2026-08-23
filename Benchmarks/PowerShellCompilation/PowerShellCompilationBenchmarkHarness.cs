using PowerForge.Compiled;

namespace PowerForge.CompilationBenchmarks;

public static class PowerShellCompilationBenchmarkHarness
{
    public static double RunTypedBranch(int calls, double baselineMs, double relativeTolerance, double absoluteToleranceMs)
    {
        var result = 0d;
        for (var index = 0; index < calls; index++)
            result = PowerForge_CompilationBenchmarkMethods.Get_AllowedAverageMs(baselineMs, relativeTolerance, absoluteToleranceMs);
        return result;
    }

    public static double RunHandWrittenBranch(int calls, double baselineMs, double relativeTolerance, double absoluteToleranceMs)
    {
        var result = 0d;
        for (var index = 0; index < calls; index++)
        {
            var relativeCap = baselineMs * (1d + relativeTolerance);
            var absoluteCap = baselineMs + absoluteToleranceMs;
            result = relativeCap > absoluteCap ? relativeCap : absoluteCap;
        }
        return result;
    }

    public static long RunTypedLoop(int calls, int count)
    {
        var result = 0L;
        for (var index = 0; index < calls; index++)
            result = PowerForge_CompilationBenchmarkMethods.Get_TriangularNumber(count);
        return result;
    }

    public static long RunHandWrittenLoop(int calls, int count)
    {
        var result = 0L;
        for (var call = 0; call < calls; call++)
        {
            var total = 0L;
            for (var value = 1; value <= count; value++)
                total += value;
            result = total;
        }
        return result;
    }

    public static long RunTypedRepeatedLoop(int calls, int count)
        => PowerForge_CompilationBenchmarkMethods.Get_RepeatedTriangularNumber(calls, count);

    public static long RunHandWrittenRepeatedLoop(int calls, int count)
    {
        var result = 0L;
        for (var call = 0; call < calls; call++)
        {
            var total = 0L;
            for (var value = 1; value <= count; value++)
                total += value;
            result = total;
        }
        return result;
    }
}
