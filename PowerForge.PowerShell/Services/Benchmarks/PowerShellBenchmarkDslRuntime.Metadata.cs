namespace PowerForge;

public static partial class PowerShellBenchmarkDslRuntime
{
    /// <summary>
    /// Adds a suite-specific provenance value to benchmark metadata artifacts.
    /// </summary>
    /// <param name="name">Metadata name without the artifact's <c>benchmark.</c> prefix.</param>
    /// <param name="value">Metadata value.</param>
    public static void Metadata(string name, string value)
    {
        var suite = RequireSuite();
        var metadataName = RequireName(name, "metadata name");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Benchmark metadata value is required.", nameof(value));
        if (suite.Metadata.ContainsKey(metadataName))
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' already defines metadata '{metadataName}'.");
        suite.Metadata.Add(metadataName, value.Trim());
    }
}
