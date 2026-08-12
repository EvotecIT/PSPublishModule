using System.IO.Compression;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static void UpdateResolvedModuleVersion(
        PowerForgeModuleReleasePlanSummary? plan,
        IEnumerable<string>? artifactPaths = null)
    {
        if (plan is null)
            return;

        var manifestText = ReadBuiltModuleManifestText(plan, artifactPaths) ?? ReadSourceModuleManifestText(plan);
        if (string.IsNullOrWhiteSpace(manifestText))
            return;

        ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText!, "ModuleVersion", out var moduleVersion);
        if (NormalizeReleaseVersion(plan.ModuleVersion) is null && !string.IsNullOrWhiteSpace(moduleVersion))
            plan.ModuleVersion = moduleVersion!.Trim();

        if (string.IsNullOrWhiteSpace(plan.PreReleaseTag))
        {
            plan.PreReleaseTag = ModuleManifestValueReader
                .ReadPsDataStringOrArrayFromText(manifestText!, "Prerelease")
                .FirstOrDefault();
        }
    }

    internal static string? ResolveUnifiedReleaseVersion(
        PowerForgeReleaseGitHubOptions options,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
    {
        var packageVersion = NormalizeReleaseVersion(sharedReleaseVersion);
        var moduleVersion = ResolveModuleReleaseVersion(result.ModulePlan);
        var assetVersion = ResolveUniqueAssetVersion(result.ReleaseAssetEntries);

        return options.VersionSource switch
        {
            PowerForgeReleaseVersionSource.Module => moduleVersion,
            PowerForgeReleaseVersionSource.Packages => packageVersion,
            PowerForgeReleaseVersionSource.Assets => assetVersion,
            _ => packageVersion ?? moduleVersion ?? assetVersion
        };
    }

    private static string? ResolveModuleReleaseVersion(PowerForgeModuleReleasePlanSummary? plan)
    {
        var moduleVersion = NormalizeReleaseVersion(plan?.ModuleVersion);
        if (string.IsNullOrWhiteSpace(moduleVersion))
            return null;

        var preReleaseTag = plan?.PreReleaseTag?.Trim().TrimStart('-');
        if (string.IsNullOrWhiteSpace(preReleaseTag) || moduleVersion!.Contains('-'))
            return moduleVersion;

        return moduleVersion + "-" + preReleaseTag;
    }

    private static bool IsModuleArtifactForResolvedVersion(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        var resolvedVersion = ResolveModuleReleaseVersion(plan);
        if (string.IsNullOrWhiteSpace(resolvedVersion))
            return true;

        var manifestName = Path.GetFileName(plan?.ManifestPath);
        if (!string.IsNullOrWhiteSpace(manifestName) &&
            string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var manifestText = ReadModuleManifestText(path, manifestName!);
            if (!string.IsNullOrWhiteSpace(manifestText))
            {
                var artifactVersion = ResolveModuleManifestVersion(manifestText!);
                return string.Equals(artifactVersion, resolvedVersion, StringComparison.OrdinalIgnoreCase);
            }
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        return Regex.IsMatch(
            fileName,
            $@"(?:^|[._-])v?{Regex.Escape(resolvedVersion!)}(?:$|[._-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool IsFinalPowerShellModulePackage(
        string path,
        PowerForgeModuleReleasePlanSummary? plan)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 50000)
                return false;

            var files = archive.Entries
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(static entry => entry.FullName.Replace('\\', '/').TrimStart('/'))
                .ToArray();
            if (files.Length == 0 ||
                files.Any(static name =>
                    name.Split('/').Any(static segment => segment is "." or "..") ||
                    name.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/.github/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var expectedManifestName = Path.GetFileName(plan?.ManifestPath);
            if (!string.IsNullOrWhiteSpace(expectedManifestName))
            {
                var moduleName = string.IsNullOrWhiteSpace(plan?.ModuleName)
                    ? Path.GetFileNameWithoutExtension(expectedManifestName)
                    : plan!.ModuleName!.Trim();
                var expectedManifestPaths = new[] { string.Empty }
                    .Concat(plan?.PackedModuleRoots ?? Array.Empty<string>())
                    .Select(NormalizePackedModuleRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(root => string.IsNullOrWhiteSpace(root)
                        ? $"{moduleName}/{expectedManifestName}"
                        : $"{root}/{moduleName}/{expectedManifestName}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return files.Count(expectedManifestPaths.Contains) == 1;
            }

            var roots = files
                .Select(static name => name.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var moduleRoots = roots.Count(root =>
                files.Contains(root + "/" + root + ".psd1", StringComparer.OrdinalIgnoreCase));
            return moduleRoots == 1;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves archive-relative roots that can contain the main module in configured packed artefacts.
    /// </summary>
    internal static string[] ResolvePackedModuleRoots(
        ModulePipelineConfigurationContext? context,
        string? moduleName,
        string? moduleVersion,
        string? preRelease)
    {
        if (context is null || string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(moduleVersion))
            return Array.Empty<string>();

        return (context.Spec.Segments ?? Array.Empty<IConfigurationSegment>())
            .OfType<ConfigurationArtefactSegment>()
            .Where(segment => segment.ArtefactType == ArtefactType.Packed && segment.Configuration?.Enabled == true)
            .Select(segment => ResolvePackedModuleRoot(
                segment.Configuration,
                context.ProjectRoot,
                moduleName!,
                moduleVersion!,
                preRelease))
            .OfType<string>()
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolvePackedModuleRoot(
        ArtefactConfiguration configuration,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var configuredPath = FirstNonEmpty(
            configuration.RequiredModules?.ModulesPath,
            configuration.RequiredModules?.Path);
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var replaced = ModulePathTokenFormatter.ReplacePathTokens(
            configuredPath,
            moduleName,
            moduleVersion,
            preRelease);
        replaced = PathValueResolver.Clean(replaced);
        if (string.IsNullOrWhiteSpace(replaced))
            return null;

        if (!Path.IsPathRooted(replaced))
            return NormalizePackedModuleRoot(replaced);

        var outputRoot = ArtefactLayoutPathResolver.ResolveOutputRoot(
            configuration.Path,
            projectRoot,
            moduleName,
            moduleVersion,
            preRelease,
            ArtefactType.Packed);
        var relative = FrameworkCompatibility.GetRelativePath(outputRoot, Path.GetFullPath(replaced));
        var normalized = NormalizePackedModuleRoot(relative);
        if (normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Packed artefact ModulesPath '{configuredPath}' must remain under the packed artefact output root.");
        }

        return normalized;
    }

    private static string NormalizePackedModuleRoot(string value)
        => value.Trim().Replace('\\', '/').Trim('/');

    private static string? ResolveModuleManifestVersion(string manifestText)
    {
        if (!ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, "ModuleVersion", out var value))
            return null;

        var version = NormalizeReleaseVersion(value);
        if (string.IsNullOrWhiteSpace(version) || version!.Contains('-'))
            return version;

        var preReleaseTag = ModuleManifestValueReader
            .ReadPsDataStringOrArrayFromText(manifestText, "Prerelease")
            .FirstOrDefault()?
            .Trim()
            .TrimStart('-');
        return string.IsNullOrWhiteSpace(preReleaseTag)
            ? version
            : version + "-" + preReleaseTag;
    }

    private static string? ResolveUniqueAssetVersion(IEnumerable<PowerForgeReleaseAssetEntry> entries)
    {
        var versions = entries
            .Select(entry => NormalizeReleaseVersion(entry.Version))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return versions.Length == 1 ? versions[0] : null;
    }

    private static string? NormalizeReleaseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var version = value!.Trim();
        var labelStart = version.IndexOfAny(new[] { '-', '+' });
        var coreVersion = labelStart >= 0 ? version.Substring(0, labelStart) : version;
        var hasPlaceholder = coreVersion
            .Split('.')
            .Any(component => string.Equals(component, "X", StringComparison.OrdinalIgnoreCase) || component == "*");
        return hasPlaceholder
            ? null
            : version;
    }

    private static string? ReadBuiltModuleManifestText(
        PowerForgeModuleReleasePlanSummary plan,
        IEnumerable<string>? artifactPaths)
    {
        var manifestName = Path.GetFileName(plan.ManifestPath);
        if (string.IsNullOrWhiteSpace(manifestName) || artifactPaths is null)
            return null;

        foreach (var path in EnumerateModuleManifestCandidates(artifactPaths, manifestName!)
            .OrderByDescending(GetLastWriteTimeUtcSafe))
        {
            var manifestText = ReadModuleManifestText(path, manifestName!);
            if (string.IsNullOrWhiteSpace(manifestText))
                continue;

            if (ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText!, "ModuleVersion", out var version) &&
                NormalizeReleaseVersion(version) is not null)
            {
                return manifestText;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateModuleManifestCandidates(
        IEnumerable<string> artifactPaths,
        string manifestName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuredPath in artifactPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            foreach (var path in PathTokenCandidateResolver.ResolveExistingPaths(configuredPath)
                .OrderByDescending(PathTokenCandidateResolver.GetLatestWriteTimeUtc)
                .ThenByDescending(candidate => candidate, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                {
                    if (seen.Add(path))
                        yield return path;
                    continue;
                }

                if (!Directory.Exists(path))
                    continue;

                foreach (var file in Directory.EnumerateFiles(path, "*.zip", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(path, manifestName, SearchOption.AllDirectories)))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath))
                        yield return fullPath;
                }
            }
        }
    }

    private static string? ReadModuleManifestText(string path, string manifestName)
    {
        try
        {
            if (string.Equals(Path.GetFileName(path), manifestName, StringComparison.OrdinalIgnoreCase))
                return File.ReadAllText(path);

            if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                return null;

            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries
                .Where(candidate => string.Equals(candidate.Name, manifestName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.FullName.Length)
                .FirstOrDefault();
            if (entry is null)
                return null;

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadSourceModuleManifestText(PowerForgeModuleReleasePlanSummary plan)
    {
        if (string.IsNullOrWhiteSpace(plan.ManifestPath) || !File.Exists(plan.ManifestPath))
            return null;

        try
        {
            return File.ReadAllText(plan.ManifestPath!);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime GetLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
