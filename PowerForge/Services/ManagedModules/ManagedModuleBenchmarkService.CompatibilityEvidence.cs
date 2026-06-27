namespace PowerForge;

public sealed partial class ManagedModuleBenchmarkService
{
    private sealed class CompatibilityEvidence
    {
        internal string? ExpectedVersion { get; set; }

        internal string? ModulePath { get; set; }

        internal int FileCount { get; set; }

        internal long FinalDiskBytes { get; set; }

        internal string? ValidatedVersion { get; set; }

        internal bool? VersionValidationSucceeded { get; set; }

        internal string? VersionValidationMessage { get; set; }
    }

    private static CompatibilityEvidence CreateCompatibilityEvidence(
        ManagedModuleBenchmarkScenario scenario,
        ModuleDependencyInstallResult result)
    {
        var evidence = new CompatibilityEvidence
        {
            ExpectedVersion = result.ResolvedVersion,
            FinalDiskBytes = MeasureDirectoryBytes(scenario.ModuleRoot)
        };
        var manifest = FindCompatibilityManifest(scenario.ModuleRoot, scenario.Name, result.ResolvedVersion);
        if (manifest is null)
        {
            evidence.VersionValidationMessage = "Installed module manifest was not found under the compatibility benchmark sandbox.";
            return evidence;
        }

        evidence.ModulePath = manifest.DirectoryName;
        evidence.FileCount = CountFiles(manifest.DirectoryName);
        evidence.ValidatedVersion = ReadManifestVersion(manifest.FullName);
        if (string.IsNullOrWhiteSpace(result.ResolvedVersion) || string.IsNullOrWhiteSpace(evidence.ValidatedVersion))
        {
            evidence.VersionValidationMessage = "Compatibility run completed, but version validation did not have both expected and actual versions.";
            return evidence;
        }

        evidence.VersionValidationSucceeded = ManagedModuleVersionComparer.Instance.Compare(
            evidence.ValidatedVersion!,
            result.ResolvedVersion!) == 0;
        evidence.VersionValidationMessage = evidence.VersionValidationSucceeded == true
            ? "Validated native-installed manifest version " + evidence.ValidatedVersion + "."
            : "Native-installed manifest version " + evidence.ValidatedVersion + " does not match expected " + result.ResolvedVersion + ".";
        return evidence;
    }

    private static CompatibilityEvidence CreateFailedRunEvidence(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        Exception exception)
    {
        if (engine == ManagedModuleBenchmarkEngine.Managed ||
            scenario.Operation is not (ManagedModuleBenchmarkOperation.Install or ManagedModuleBenchmarkOperation.Update))
        {
            return new CompatibilityEvidence();
        }

        var expectedVersion = ResolveExpectedCompatibilityVersion(scenario);
        return CreateCompatibilityEvidence(
            scenario,
            new ModuleDependencyInstallResult(
                scenario.Name,
                installedVersion: null,
                resolvedVersion: expectedVersion,
                requestedVersion: expectedVersion,
                ModuleDependencyInstallStatus.Failed,
                engine.ToString(),
                exception.GetBaseException().Message));
    }

    private static string? ResolveExpectedCompatibilityVersion(ManagedModuleBenchmarkScenario scenario)
        => string.IsNullOrWhiteSpace(scenario.Version) ? null : scenario.Version;

    private static FileInfo? FindCompatibilityManifest(string? sandboxRoot, string moduleName, string? resolvedVersion)
    {
        if (string.IsNullOrWhiteSpace(sandboxRoot) || !Directory.Exists(sandboxRoot))
            return null;

        var files = new DirectoryInfo(sandboxRoot!)
            .EnumerateFiles(moduleName.Trim() + ".psd1", SearchOption.AllDirectories)
            .Where(file => IsCompatibilityManifestCandidate(file, sandboxRoot!))
            .ToArray();
        if (files.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(resolvedVersion))
        {
            var exact = files.FirstOrDefault(file =>
                string.Equals(ReadManifestVersion(file.FullName), resolvedVersion, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        return files
            .OrderBy(file => ReadManifestVersion(file.FullName) ?? string.Empty, ManagedModuleVersionComparer.Instance)
            .LastOrDefault();
    }

    private static bool IsCompatibilityManifestCandidate(FileInfo file, string sandboxRoot)
    {
        if (!file.FullName.StartsWith(Path.GetFullPath(sandboxRoot), StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = file.FullName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(segment =>
            string.Equals(segment, "source", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "pkg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "feed", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadManifestVersion(string manifestPath)
    {
        var version = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease").FirstOrDefault();
        return string.IsNullOrWhiteSpace(prerelease) || version!.IndexOf("-", StringComparison.Ordinal) >= 0
            ? version
            : version + "-" + prerelease;
    }

    private static int CountFiles(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        try
        {
            return Directory.EnumerateFiles(directory!, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }
}
