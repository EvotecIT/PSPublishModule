namespace PowerForge;

internal static class ManagedModuleConcurrencyDefaults
{
    internal const int MaximumDefaultConcurrency = 96;
    private const int MinimumDefaultConcurrency = 16;
    private const int ConnectionsPerProcessor = 8;

    internal static int ResolveDefault()
        => Math.Min(
            MaximumDefaultConcurrency,
            Math.Max(MinimumDefaultConcurrency, Environment.ProcessorCount * ConnectionsPerProcessor));
}
