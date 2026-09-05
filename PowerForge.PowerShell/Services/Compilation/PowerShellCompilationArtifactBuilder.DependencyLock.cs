using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void ValidateExpectedDependencyLock(
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationDependencyGraph actual)
    {
        PowerShellCompilationDependencyLockHasher.EnsureValid(actual, "actual");
        VerifyDependencyInputsHaveNotDrifted(spec, actual);
        var expected = spec.ExpectedDependencyLock;
        if (expected is null)
        {
            if (spec.AllowUnreviewedDependencyResolution) return;
            throw new InvalidOperationException(
                "PowerShell compilation requires a separately reviewed dependency lock. Supply ExpectedDependencyLock from analysis, or explicitly set AllowUnreviewedDependencyResolution for a non-reviewed development build.");
        }

        PowerShellCompilationDependencyLockHasher.EnsureValid(expected, nameof(spec.ExpectedDependencyLock));
        if (expected.SchemaVersion != actual.SchemaVersion ||
            !expected.RootNodeId.Equals(actual.RootNodeId, StringComparison.Ordinal) ||
            !expected.LockSha256.Equals(actual.LockSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PowerShell compilation dependency lock drifted. Expected {expected.LockSha256}, resolved {actual.LockSha256}. {DescribeDependencyLockDrift(expected, actual)}");
        }
    }

    private static string DescribeDependencyLockDrift(
        PowerShellCompilationDependencyGraph expected,
        PowerShellCompilationDependencyGraph actual)
    {
        if (expected.SchemaVersion != actual.SchemaVersion)
            return $"Schema changed from {expected.SchemaVersion} to {actual.SchemaVersion}.";
        if (!expected.RootNodeId.Equals(actual.RootNodeId, StringComparison.Ordinal))
            return $"Root identity changed from '{expected.RootNodeId}' to '{actual.RootNodeId}'.";
        var expectedIds = expected.Nodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        var actualIds = actual.Nodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        var removed = expected.Nodes.Where(node => !actualIds.Contains(node.Id)).Take(3)
            .Select(static node => $"removed {node.Kind}:{node.Identity.Source}");
        var added = actual.Nodes.Where(node => !expectedIds.Contains(node.Id)).Take(3)
            .Select(static node => $"added {node.Kind}:{node.Identity.Source}");
        var differences = removed.Concat(added).ToArray();
        if (differences.Length > 0) return string.Join("; ", differences) + ".";
        if (expected.Edges.Length != actual.Edges.Length)
            return $"Edge count changed from {expected.Edges.Length} to {actual.Edges.Length}.";
        return "Canonical graph content changed without a node-set change.";
    }

    private static void VerifyDependencyInputsHaveNotDrifted(
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationDependencyGraph graph)
    {
        var moduleRoot = Path.GetDirectoryName(Path.GetFullPath(spec.ModuleManifestPath ?? spec.SourcePath))
                         ?? Directory.GetCurrentDirectory();
        foreach (var node in graph.Nodes.Where(static node => node.Exists && node.Identity.Sha256.Length > 0))
        {
            var source = node.Identity.Source.Replace('/', Path.DirectorySeparatorChar);
            var path = node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal)
                ? ResolveRuntimePackAssetPath(node.Identity, spec.NuGetPackageRoot)
                : Path.IsPathRooted(source) ? Path.GetFullPath(source) : Path.GetFullPath(Path.Combine(moduleRoot, source));
            if (!File.Exists(path))
                throw new InvalidOperationException($"Locked PowerShell compilation input '{node.Identity.Source}' disappeared after analysis.");
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var actual = string.Concat(sha.ComputeHash(stream)
                .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            if (!actual.Equals(node.Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Locked PowerShell compilation input '{node.Identity.Source}' changed after analysis.");
        }
    }

    private static string ResolveRuntimePackAssetPath(PowerShellCompilationDependencyIdentity identity, string? configuredPackageRoot)
    {
        var segments = identity.Source.Split('/');
        if (segments.Length is not (4 or 5) || !segments[0].Equals("runtime-pack", StringComparison.Ordinal) ||
            (segments.Length == 5 && !segments[3].Equals("native", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Locked runtime-pack source '{identity.Source}' is malformed.");
        var packageRoot = string.IsNullOrWhiteSpace(configuredPackageRoot)
            ? Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            : configuredPackageRoot;
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return Path.GetFullPath(Path.Combine(
            packageRoot!,
            segments[1],
            segments[2],
            "runtimes",
            identity.RuntimeIdentifier,
              segments.Length == 5 ? "native" : "lib",
              segments.Length == 5 ? segments[4] : Path.Combine(identity.TargetFramework, segments[3])));
    }
}
