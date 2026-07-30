namespace PowerForge;

internal static class BenchmarkEvidenceMetadataPolicy
{
    private static readonly string[] ExecutionPolicyMetadataKeys =
    {
        "profile",
        "cleanup",
        "warmupCount",
        "iterationCount",
        "runOrder",
        "memoryCleanup",
        "cooldownMilliseconds",
        "outlierMode",
        "runMode"
    };

    internal static bool IsCompatibilityKey(string key)
    {
        return key.StartsWith("benchmark.fixture.", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("benchmark.package.", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("benchmark.workload.", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("benchmark.execution.", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("benchmark.runner.", StringComparison.OrdinalIgnoreCase) ||
               ExecutionPolicyMetadataKeys.Contains(key, StringComparer.OrdinalIgnoreCase) ||
               key.Equals("gitSha", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("psEdition", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("powerShellVersion", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsProvenanceBoundKey(string key)
    {
        return IsCompatibilityKey(key) ||
               key.StartsWith("benchmark.provenance.", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("gitBranch", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("gitWorktreeClean", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("importedUtc", StringComparison.OrdinalIgnoreCase);
    }
}
