namespace PowerForge;

internal static class ManagedModuleBenchmarkEvidence
{
    internal static ManagedModuleBenchmarkRunEvidence FromInstall(ManagedModuleInstallResult result)
    {
        var installs = FlattenInstallResults(result).ToArray();
        var validation = ValidateVersion(result.Name, result.Version, result.ModulePath);
        return CreateEvidence(installs, CountDependencies(result), result.ModulePath, validation);
    }

    internal static ManagedModuleBenchmarkRunEvidence FromUpdate(ManagedModuleUpdateResult result)
    {
        var installs = new List<ManagedModuleInstallResult>();
        var dependencyCount = 0;
        if (result.InstallResult is not null)
        {
            installs.AddRange(FlattenInstallResults(result.InstallResult));
            dependencyCount += CountDependencies(result.InstallResult);
        }

        foreach (var family in result.FamilyResults ?? Array.Empty<ManagedModuleFamilyUpdateResult>())
        {
            if (family.InstallResult is not null)
            {
                installs.AddRange(FlattenInstallResults(family.InstallResult));
                dependencyCount += CountDependencies(family.InstallResult);
            }
        }

        var validation = ValidateVersion(result.Name, result.TargetVersion, result.ModulePath);
        return CreateEvidence(installs, dependencyCount, result.ModulePath, validation);
    }

    private static ManagedModuleBenchmarkRunEvidence CreateEvidence(
        IReadOnlyList<ManagedModuleInstallResult> installs,
        int dependencyCount,
        string? modulePath,
        VersionValidation validation)
    {
        var downloads = installs
            .Select(static install => install.Download)
            .Where(static download => download is not null)
            .Select(static download => download!)
            .ToArray();

        return new ManagedModuleBenchmarkRunEvidence
        {
            DependencyCount = dependencyCount,
            PackageCount = downloads.Length,
            TotalPackageBytes = downloads.Sum(static download => download.BytesWritten),
            TotalExtractedBytes = installs.Sum(static install => install.ExtractedBytes),
            TotalFileCount = installs.Sum(static install => install.FileCount),
            FinalDiskBytes = MeasureDirectoryBytes(modulePath),
            ValidatedVersion = validation.ValidatedVersion,
            VersionValidationSucceeded = validation.Succeeded,
            VersionValidationMessage = validation.Message
        };
    }

    private static int CountDependencies(ManagedModuleInstallResult result)
        => (result.DependencyResults ?? Array.Empty<ManagedModuleInstallResult>())
            .Sum(static dependency => 1 + CountDependencies(dependency));

    private static IEnumerable<ManagedModuleInstallResult> FlattenInstallResults(ManagedModuleInstallResult result)
    {
        yield return result;

        foreach (var dependency in result.DependencyResults ?? Array.Empty<ManagedModuleInstallResult>())
        {
            foreach (var nested in FlattenInstallResults(dependency))
                yield return nested;
        }
    }

    private static VersionValidation ValidateVersion(string moduleName, string expectedVersion, string? modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            return VersionValidation.Unknown("Module directory was not found after the measured operation.");

        var manifest = FindManifest(moduleName, modulePath!);
        if (manifest is null)
            return ValidateFolderVersion(expectedVersion, modulePath!);

        var manifestVersion = ModuleManifestValueReader.ReadTopLevelString(manifest.FullName, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(manifestVersion))
            return VersionValidation.Unknown($"Module manifest '{manifest.FullName}' does not declare ModuleVersion.");

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifest.FullName, "Prerelease").FirstOrDefault();
        var actualVersion = CombineVersion(manifestVersion!, prerelease);
        return VersionsMatch(actualVersion, expectedVersion)
            ? VersionValidation.Success(actualVersion, $"Validated manifest version {actualVersion}.")
            : VersionValidation.Failed(actualVersion, $"Manifest version {actualVersion} does not match expected {expectedVersion}.");
    }

    private static VersionValidation ValidateFolderVersion(string expectedVersion, string modulePath)
    {
        var folderVersion = Path.GetFileName(modulePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderVersion))
            return VersionValidation.Unknown("Module directory did not expose a version folder name.");

        return VersionsMatch(folderVersion!, expectedVersion)
            ? VersionValidation.Success(folderVersion!, $"Validated version folder {folderVersion}.")
            : VersionValidation.Failed(folderVersion!, $"Version folder {folderVersion} does not match expected {expectedVersion}.");
    }

    private static FileInfo? FindManifest(string moduleName, string modulePath)
    {
        var preferred = Path.Combine(modulePath, moduleName.Trim() + ".psd1");
        if (File.Exists(preferred))
            return new FileInfo(preferred);

        try
        {
            return new DirectoryInfo(modulePath)
                .EnumerateFiles("*.psd1", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static long MeasureDirectoryBytes(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        try
        {
            return Directory.EnumerateFiles(directory!, "*", SearchOption.AllDirectories)
                .Sum(static path => new FileInfo(path).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string CombineVersion(string version, string? prerelease)
    {
        var trimmedVersion = version.Trim();
        if (string.IsNullOrWhiteSpace(prerelease) || trimmedVersion.IndexOf("-", StringComparison.Ordinal) >= 0)
            return trimmedVersion;

        return trimmedVersion + "-" + prerelease!.Trim().TrimStart('-');
    }

    private static bool VersionsMatch(string actualVersion, string expectedVersion)
        => ManagedModuleVersionComparer.Instance.Compare(actualVersion, expectedVersion) == 0;

    private readonly struct VersionValidation
    {
        private VersionValidation(bool? succeeded, string? validatedVersion, string message)
        {
            Succeeded = succeeded;
            ValidatedVersion = validatedVersion;
            Message = message;
        }

        internal bool? Succeeded { get; }

        internal string? ValidatedVersion { get; }

        internal string Message { get; }

        internal static VersionValidation Success(string validatedVersion, string message)
            => new(true, validatedVersion, message);

        internal static VersionValidation Failed(string validatedVersion, string message)
            => new(false, validatedVersion, message);

        internal static VersionValidation Unknown(string message)
            => new(null, null, message);
    }
}

internal sealed class ManagedModuleBenchmarkRunEvidence
{
    internal int DependencyCount { get; set; }

    internal int PackageCount { get; set; }

    internal long TotalPackageBytes { get; set; }

    internal long TotalExtractedBytes { get; set; }

    internal int TotalFileCount { get; set; }

    internal long FinalDiskBytes { get; set; }

    internal string? ValidatedVersion { get; set; }

    internal bool? VersionValidationSucceeded { get; set; }

    internal string? VersionValidationMessage { get; set; }
}
