namespace PowerForge;

internal static class BenchmarkComparisonSemantics
{
    internal static bool IsDurationMetric(string? metric)
    {
        var name = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric!.Trim();
        return name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "P95", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "P99", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "StdDev", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "StdErr", StringComparison.OrdinalIgnoreCase);
    }
}
