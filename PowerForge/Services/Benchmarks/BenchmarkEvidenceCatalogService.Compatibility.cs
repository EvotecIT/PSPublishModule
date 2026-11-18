using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    private static Dictionary<string, string> BuildCompatibility(BenchmarkRunResult result)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["suite"] = result.Suite
        };
        AddCompatibilityDimension(dimensions, "environment.runtimeVersion", result.Environment.RuntimeVersion);
        AddCompatibilityDimension(dimensions, "environment.dotNetSdkVersion", result.Environment.DotNetSdkVersion);
        AddCompatibilityDimension(dimensions, "environment.runner", result.Environment.Runner);
        AddCompatibilityDimension(dimensions, "environment.osArchitecture", result.Environment.OsArchitecture);
        AddCompatibilityDimension(dimensions, "environment.processArchitecture", result.Environment.ProcessArchitecture);
        AddCompatibilityDimension(dimensions, "environment.processorName", result.Environment.ProcessorName);
        AddCompatibilityDimension(
            dimensions,
            "environment.physicalProcessorCount",
            result.Environment.PhysicalProcessorCount?.ToString(CultureInfo.InvariantCulture));
        AddCompatibilityDimension(
            dimensions,
            "environment.physicalCoreCount",
            result.Environment.PhysicalCoreCount?.ToString(CultureInfo.InvariantCulture));
        AddCompatibilityDimension(
            dimensions,
            "environment.logicalCoreCount",
            result.Environment.LogicalCoreCount?.ToString(CultureInfo.InvariantCulture));
        AddCompatibilityDimension(
            dimensions,
            "benchmark.workload.shape.sha256",
            ComputeWorkloadShape(result));
        AddCompatibilityDimension(
            dimensions,
            "benchmark.comparison.shape.sha256",
            ComputeComparisonShape(result));
        foreach (var item in result.Metadata.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (BenchmarkEvidenceMetadataPolicy.IsCompatibilityKey(item.Key))
            {
                dimensions[item.Key] = item.Value;
            }
        }

        return dimensions;
    }

    private static string ComputeWorkloadShape(BenchmarkRunResult result)
    {
        string[] identities = result.Samples
            .Select(sample => CreateWorkloadIdentity(
                sample.Scenario,
                sample.Operation,
                sample.Engine,
                sample.Host,
                sample.Variables))
            .Concat(result.Summary.Select(row => CreateWorkloadIdentity(
                row.Scenario,
                row.Operation,
                row.Engine,
                row.Host,
                row.Variables)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (identities.Length == 0)
            return string.Empty;

        string canonical = string.Join("\n", identities);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ComputeComparisonShape(BenchmarkRunResult result)
    {
        string[] identities = result.Comparison
            .Select(row =>
            {
                var builder = new StringBuilder();
                AppendIdentityPart(builder, "scenario", row.Scenario);
                AppendIdentityPart(builder, "operation", row.Operation);
                AppendIdentityPart(builder, "host", row.Host);
                AppendIdentityPart(builder, "runMode", row.RunMode);
                AppendIdentityPart(builder, "engine", row.Engine);
                AppendIdentityPart(builder, "baselineEngine", row.BaselineEngine);
                AppendIdentityPart(builder, "metric", row.Metric);
                AppendIdentityPart(
                    builder,
                    "tieTolerance",
                    row.TieTolerance.ToString("R", CultureInfo.InvariantCulture));
                foreach (var variable in row.Variables
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    AppendIdentityPart(
                        builder,
                        variable.Key.ToUpperInvariant(),
                        variable.Value);
                }

                return builder.ToString();
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (identities.Length == 0)
            return string.Empty;

        string canonical = string.Join("\n", identities);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string CreateWorkloadIdentity(
        string scenario,
        string operation,
        string engine,
        string host,
        IReadOnlyDictionary<string, string?> variables)
    {
        var builder = new StringBuilder();
        AppendIdentityPart(builder, "scenario", scenario);
        AppendIdentityPart(builder, "operation", operation);
        AppendIdentityPart(builder, "engine", engine);
        AppendIdentityPart(builder, "host", host);
        foreach (var variable in variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            AppendIdentityPart(builder, variable.Key.ToUpperInvariant(), variable.Value);
        }

        return builder.ToString();
    }

    private static void AppendIdentityPart(StringBuilder builder, string key, string? value)
    {
        string text = value ?? "<null>";
        builder.Append(key.Length)
            .Append(':')
            .Append(key)
            .Append('=')
            .Append(text.Length)
            .Append(':')
            .Append(text)
            .Append(';');
    }

    private static void AddCompatibilityDimension(
        IDictionary<string, string> dimensions,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            dimensions[key] = value!.Trim();
    }

    private static string ResolvePlatform(BenchmarkRunResult result, string? platformOverride)
    {
        var labels = new[] { platformOverride, result.Environment.OsFamily }
            .Concat(MetadataValues(result.Metadata, "osLabel", "os"))
            .Concat(result.Samples.Select(sample => sample.Os))
            .Concat(result.Summary.Select(row => row.Os))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(BenchmarkPlatformNormalizer.NormalizeId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (labels.Length == 0)
        {
            throw new InvalidOperationException(
                "The benchmark result does not identify its operating-system platform. Supply the producing platform explicitly.");
        }

        if (labels.Length > 1)
        {
            throw new InvalidOperationException(
                $"Benchmark evidence must identify one operating-system platform; found {string.Join(", ", labels)}.");
        }

        return labels[0];
    }

    private static void ApplyCompatibility(BenchmarkEvidenceEntry[] entries)
    {
        foreach (var group in entries.GroupBy(
                     CompatibilityGroup,
                     StringComparer.OrdinalIgnoreCase))
        {
            BenchmarkEvidenceEntry[] published = group.Where(entry => entry.Publish).ToArray();
            foreach (var entry in group)
            {
                BenchmarkEvidenceEntry[] candidates = published.Length == 0
                    ? group.ToArray()
                    : entry.Publish
                        ? published
                        : published.Append(entry).ToArray();
                string[] issues = CompareDimensions(candidates);
                entry.Comparable = issues.Length == 0;
                entry.CompatibilityIssues = issues;
            }
        }
    }

    private static string[] CompareDimensions(IReadOnlyCollection<BenchmarkEvidenceEntry> entries)
    {
        var keys = entries
            .SelectMany(entry => entry.Compatibility.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        foreach (var key in keys)
        {
            var values = entries
                .Select(entry => TryGetValueIgnoreCase(entry.Compatibility, key, out var value) ? value : "<missing>")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (values.Length > 1)
                issues.Add($"{key}: lanes contain incompatible values ({string.Join(", ", values.Select(value => $"'{value}'"))}).");
        }

        return issues.ToArray();
    }
}
