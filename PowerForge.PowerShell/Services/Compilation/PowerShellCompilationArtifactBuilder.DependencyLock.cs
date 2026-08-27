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
                $"PowerShell compilation dependency lock drifted. Expected {expected.LockSha256}, resolved {actual.LockSha256}.");
        }
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
            var path = Path.IsPathRooted(source) ? Path.GetFullPath(source) : Path.GetFullPath(Path.Combine(moduleRoot, source));
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
}
