using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsGeneratedBuildInfrastructurePath(
        string path,
        IEnumerable<string>? generatedBuildRoots)
    {
        if (!IsBelowGeneratedBuildRoot(path, generatedBuildRoots))
            return false;

        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBelowGeneratedBuildRoot(
        string path,
        IEnumerable<string>? generatedBuildRoots)
        => (generatedBuildRoots ?? Array.Empty<string>())
            .Any(root => IsSameOrBelowBuildInputPath(path, root));

    private static bool IsTrustedGeneratedOutputPath(
        string path,
        IEnumerable<string>? outputRoots,
        IEnumerable<string>? expectedOutputPaths,
        string referencedProjectDirectory,
        IEnumerable<string> evaluatedProjectDirectories)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            StringComparison comparison = IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!(expectedOutputPaths ?? Array.Empty<string>()).Any(expectedPath =>
                    string.Equals(Path.GetFullPath(expectedPath), fullPath, comparison)))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        foreach (string root in outputRoots ?? Array.Empty<string>())
        {
            try
            {
                string traversalBoundary = FindCommonBuildInputPathRoot(
                    root,
                    referencedProjectDirectory);
                if (!IsSameOrBelowBuildInputPath(path, root) ||
                    evaluatedProjectDirectories.Any(projectDirectory =>
                        IsSameOrBelowBuildInputPath(projectDirectory, root)) ||
                    IsTrackedProjectOutputPath(path, referencedProjectDirectory) ||
                    !HasSinglePhysicalLink(path) ||
                    IsReparsePointPath(root) ||
                    IsReparsePointPath(traversalBoundary) ||
                    HasReparsePointBelowRoot(path, traversalBoundary))
                {
                    continue;
                }

                return true;
            }
            catch
            {
                // Generated outputs are trusted only when physical containment can be proven.
            }
        }

        return false;
    }

    private static bool IsTrustedSdkGeneratedPublishInput(
        string path,
        IEnumerable<string>? outputRoots,
        string projectDirectory,
        IEnumerable<string> evaluatedProjectDirectories)
    {
        foreach (string root in outputRoots ?? Array.Empty<string>())
        {
            try
            {
                string traversalBoundary = FindCommonBuildInputPathRoot(root, projectDirectory);
                if (!IsSameOrBelowBuildInputPath(path, root) ||
                    evaluatedProjectDirectories.Any(directory =>
                        IsSameOrBelowBuildInputPath(directory, root)) ||
                    IsTrackedProjectOutputPath(path, projectDirectory) ||
                    !HasSinglePhysicalLink(path) ||
                    IsReparsePointPath(root) ||
                    IsReparsePointPath(traversalBoundary) ||
                    HasReparsePointBelowRoot(path, traversalBoundary))
                {
                    continue;
                }

                return true;
            }
            catch
            {
                // SDK-generated publish inputs are trusted only with proven physical containment.
            }
        }

        return false;
    }

    private static bool HasSinglePhysicalLink(string path)
    {
        try
        {
            return ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { path })[0] == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryProveControlledGeneratedOutputs(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> candidatePaths,
        IReadOnlyCollection<string> evaluatedBuildInputs,
        IReadOnlyCollection<string> evaluatedMsBuildInputs,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? evaluatedPathMap,
        VerifiedPackageInputCatalog? verifiedPackages,
        IDictionary<string, bool> cache)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string[] fullCandidatePaths = candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .ToArray();
        if (fullCandidatePaths.Length == 0)
            return true;

        bool allCached = true;
        foreach (string fullCandidatePath in fullCandidatePaths)
        {
            if (!cache.TryGetValue(BuildCacheKey(fullCandidatePath), out bool cachedResult))
            {
                allCached = false;
                continue;
            }
            if (!cachedResult)
                return false;
        }
        if (allCached)
            return true;

        string controlledOutputRoot = Path.Combine(
            Path.GetTempPath(),
            "powerforge-provenance-build-" + Guid.NewGuid().ToString("N"));
        string controlledSourceRoot = Path.Combine(controlledOutputRoot, "source");
        string? controlledGitRoot = null;
        try
        {
            if (fullCandidatePaths.Any(path => !File.Exists(path)))
                return CacheAll(false);
            Directory.CreateDirectory(controlledOutputRoot);
            IReadOnlyDictionary<string, string> effectiveGlobalProperties =
                request.ReadEffectiveGlobalProperties();
            IReadOnlyDictionary<string, string> controlledEvaluationProperties =
                request.BuildControlledEvaluationProperties(evaluatedProperties);
            if (!TryCreateControlledSourceCheckout(
                    request.ProjectPath,
                    controlledSourceRoot,
                    evaluatedBuildInputs,
                    evaluatedMsBuildInputs,
                    effectiveGlobalProperties,
                    new Dictionary<string, IReadOnlyDictionary<string, string>[]>(
                        IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                    {
                        [Path.GetFullPath(request.ProjectPath)] = [controlledEvaluationProperties]
                    },
                    out controlledGitRoot,
                    out string? controlledProjectPath))
            {
                return CacheAll(false);
            }
            if (!TryCreateControlledBuildEnvironment(
                    request.EnvironmentVariables,
                    request.ControlledBuildEnvironmentVariableNames,
                    controlledGitRoot!,
                    controlledSourceRoot,
                    Path.GetDirectoryName(request.ProjectPath)!,
                    out IReadOnlyDictionary<string, string?> controlledEnvironment))
            {
                return CacheAll(false);
            }
            string offlinePackageSource = Directory.CreateDirectory(
                Path.Combine(controlledOutputRoot, "packages-source")).FullName;
            string[] offlinePackageSources = { offlinePackageSource };
            if (verifiedPackages is not null &&
                !verifiedPackages.TrySeedControlledPackageSource(
                    offlinePackageSource,
                    controlledSourceRoot,
                    controlledProjectPath!,
                    request.TrustedBuildPackages,
                    out offlinePackageSources,
                    out _,
                    allowSdkManagedToolchainPackages: true))
                return CacheAll(false);
            string controlledNuGetConfig = Path.Combine(controlledOutputRoot, "NuGet.Config");
            new XDocument(
                new XElement("configuration",
                    new XElement("packageSources",
                        new XElement("clear"),
                        offlinePackageSources.Select((source, index) =>
                            new XElement("add",
                                new XAttribute("key", "verified-" + index),
                                new XAttribute("value", source)))),
                    new XElement("auditSources", new XElement("clear"))))
                .Save(controlledNuGetConfig);
            string offlinePackageSourceList = string.Join(";", offlinePackageSources);
            var arguments = new List<string>
            {
                "msbuild",
                controlledProjectPath!,
                "-nologo",
                "-verbosity:quiet",
                "-restore",
                "-target:Build",
                "-getItem:FileWrites"
            };
            if (request.Configuration is not null)
            {
                if (!TryRemapControlledBuildValue(
                        request.Configuration,
                        controlledGitRoot!,
                        controlledSourceRoot,
                        Path.GetDirectoryName(request.ProjectPath)!,
                        out string controlledConfiguration))
                {
                    return CacheAll(false);
                }
                arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(controlledConfiguration));
            }
            if (request.HasExplicitTargetFramework)
            {
                if (!TryRemapControlledBuildValue(
                        request.TargetFramework!,
                        controlledGitRoot!,
                        controlledSourceRoot,
                        Path.GetDirectoryName(request.ProjectPath)!,
                        out string controlledTargetFramework))
                {
                    return CacheAll(false);
                }
                arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(controlledTargetFramework));
            }
            foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("PathMap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!TryRemapControlledBuildValue(
                        property.Value,
                        controlledGitRoot!,
                        controlledSourceRoot,
                        Path.GetDirectoryName(request.ProjectPath)!,
                        out string controlledValue))
                {
                    return CacheAll(false);
                }
                arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(controlledValue));
            }
            arguments.Add("-p:BuildProjectReferences=true");
            string controlledPathMap = evaluatedPathMap ?? string.Empty;
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    controlledGitRoot!,
                    controlledPathMap,
                    out controlledPathMap))
            {
                return CacheAll(false);
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));
            AppendControlledProofSafeguards(
                arguments,
                controlledNuGetConfig,
                offlinePackageSourceList);

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath!)!,
                arguments,
                controlledEnvironment,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
                return CacheAll(false);

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return CacheAll(false);
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Items", out JsonElement items) ||
                !items.TryGetProperty("FileWrites", out JsonElement fileWrites) ||
                fileWrites.ValueKind != JsonValueKind.Array)
            {
                return CacheAll(false);
            }

            bool allEquivalent = true;
            foreach (string fullCandidatePath in fullCandidatePaths)
            {
                bool equivalent = false;
                if (IsSameOrBelowBuildInputPath(fullCandidatePath, controlledGitRoot!))
                {
                    string relativePath = FrameworkCompatibility.GetRelativePath(
                        controlledGitRoot!,
                        fullCandidatePath);
                    string controlledCandidatePath = Path.GetFullPath(
                        Path.Combine(controlledSourceRoot, relativePath));
                    equivalent = IsSameOrBelowBuildInputPath(
                            controlledCandidatePath,
                            controlledSourceRoot) &&
                        IsSameOrBelowBuildInputPath(
                            controlledCandidatePath,
                            controlledOutputRoot) &&
                        File.Exists(controlledCandidatePath) &&
                        AreControlledGeneratedOutputsEquivalent(
                            fullCandidatePath,
                            controlledCandidatePath);
                }

                cache[BuildCacheKey(fullCandidatePath)] = equivalent;
                allEquivalent &= equivalent;
            }

            return allEquivalent;
        }
        catch
        {
            // A generated output is trusted only when the controlled rebuild proves it.
            return CacheAll(false);
        }
        finally
        {
            RemoveControlledSourceCheckout(controlledGitRoot, controlledSourceRoot);
            try
            {
                if (Directory.Exists(controlledOutputRoot))
                    Directory.Delete(controlledOutputRoot, recursive: true);
            }
            catch
            {
                // Temporary proof output is best-effort cleanup only.
            }
        }

        string BuildCacheKey(string path)
            => request.BuildVisitKey() + "\0" +
               (IsWindows() ? path.ToUpperInvariant() : path);

        bool CacheAll(bool result)
        {
            foreach (string path in fullCandidatePaths)
                cache[BuildCacheKey(path)] = result;
            return result;
        }
    }

    internal static void AppendControlledProofSafeguards(
        ICollection<string> arguments,
        string nuGetConfig,
        string packageSource)
    {
        arguments.Add("-noAutoResponse");
        arguments.Add("-maxCpuCount:1");
        arguments.Add("-nodeReuse:false");
        arguments.Add("-p:RestoreConfigFile=" + EscapeMsBuildPropertyValue(nuGetConfig));
        arguments.Add("-p:RestoreSources=" + EscapeMsBuildPropertyValue(packageSource));
        arguments.Add("-p:RestoreAdditionalProjectSources=");
        arguments.Add("-p:RestoreFallbackFolders=");
        arguments.Add("-p:RestoreNoCache=true");
        arguments.Add("-p:RestoreIgnoreFailedSources=false");
        arguments.Add("-p:RestoreLockedMode=false");
        arguments.Add("-p:RestoreForceEvaluate=true");
        arguments.Add("-p:NuGetAudit=false");
        arguments.Add("-p:RunAnalyzers=false");
        arguments.Add("-p:RunAnalyzersDuringBuild=false");
        arguments.Add("-p:RunAnalyzersDuringLiveAnalysis=false");
        arguments.Add("-p:PreBuildEvent=");
        arguments.Add("-p:PostBuildEvent=");
        arguments.Add("-p:RunPostBuildEvent=Never");
        arguments.Add("-p:UseSharedCompilation=false");
        arguments.Add("-p:ComReferenceExecuteAsTool=false");
        arguments.Add("-p:ExecuteAsTool=false");
        arguments.Add("-p:ResGenExecuteAsTool=false");
        arguments.Add("-p:ResGenEnvironment=");
        arguments.Add("-p:ResgenToolPath=");
        arguments.Add("-p:ResolveComReferenceEnvironment=");
        arguments.Add("-p:ResolveComReferenceToolPath=");
        arguments.Add("-p:WinMDExpEnvironment=");
        arguments.Add("-p:WinMDExpToolPath=");
        arguments.Add("-p:LCEnvironment=");
        arguments.Add("-p:LCToolPath=");
        arguments.Add("-p:SGenEnvironment=");
        arguments.Add("-p:SGenToolPath=");
        arguments.Add("-p:AlToolPath=");
        arguments.Add("-p:AlToolExe=");
        arguments.Add("-p:CscToolPath=");
        arguments.Add("-p:CscToolExe=");
        arguments.Add("-p:VbcToolPath=");
        arguments.Add("-p:VbcToolExe=");
        arguments.Add("-p:FscToolPath=");
        arguments.Add("-p:FscToolExe=");
        arguments.Add("-p:KeyContainerName=");
    }

    private static bool TryBuildControlledPathMap(
        string controlledSourceRoot,
        string gitRoot,
        string? evaluatedPathMap,
        out string pathMap)
    {
        string originalRoot = NormalizeBuildInputPathRoot(gitRoot);
        string mappedOriginalRoot = originalRoot;
        var controlledEntries = new List<(string Source, string Target)>();
        if (!string.IsNullOrWhiteSpace(evaluatedPathMap))
        {
            foreach (string entry in evaluatedPathMap!.Split(','))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0)
                {
                    pathMap = string.Empty;
                    return false;
                }

                string source = entry.Substring(0, separator).Trim();
                string target = entry.Substring(separator + 1).Trim();
                if (source.Length == 0 || target.Length == 0)
                {
                    pathMap = string.Empty;
                    return false;
                }

                if (!Path.IsPathRooted(source))
                {
                    pathMap = string.Empty;
                    return false;
                }
                string fullSource = NormalizeBuildInputPathRoot(source);
                if (!IsSameOrBelowBuildInputPath(fullSource, originalRoot))
                {
                    pathMap = string.Empty;
                    return false;
                }
                string relativeSource = FrameworkCompatibility.GetRelativePath(originalRoot, fullSource);
                string controlledSource = NormalizeBuildInputPathRoot(
                    Path.Combine(controlledSourceRoot, relativeSource));
                if (!IsSameOrBelowBuildInputPath(controlledSource, controlledSourceRoot))
                {
                    pathMap = string.Empty;
                    return false;
                }
                controlledEntries.Add((controlledSource, target));
                if (string.Equals(
                        fullSource,
                        originalRoot,
                        IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    mappedOriginalRoot = target;
                }
            }
        }

        if (!controlledEntries.Any(entry => string.Equals(
                entry.Source,
                NormalizeBuildInputPathRoot(controlledSourceRoot),
                IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
        {
            controlledEntries.Add((NormalizeBuildInputPathRoot(controlledSourceRoot), mappedOriginalRoot));
        }
        pathMap = string.Join(
            ",",
            controlledEntries
                .OrderByDescending(entry => entry.Source.Length)
                .Select(entry => entry.Source + "=" + entry.Target));
        return true;
    }

    private static bool IsTrackedProjectOutputPath(string path, string projectDirectory)
    {
        string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (string candidateDirectory in new[] { projectDirectory, outputDirectory }
                     .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            string? gitRoot = ReadGitText(candidateDirectory, "rev-parse --show-toplevel");
            if (string.IsNullOrWhiteSpace(gitRoot))
                continue;

            string? relativePath = ToGitRelativeExclusion(candidateDirectory, gitRoot!, path);
            if (relativePath is null)
                continue;

            string? trackedOutput = ReadGitRawText(
                gitRoot!,
                $"ls-files --stage -- {QuoteLiteralGitPath(relativePath)}");
            if (trackedOutput is null || trackedOutput.Length > 0)
                return true;
        }

        return false;
    }

    private static string FindCommonBuildInputPathRoot(string firstPath, string secondPath)
    {
        string current = NormalizeBuildInputPathRoot(firstPath);
        string second = Path.GetFullPath(secondPath);
        while (!IsSameOrBelowBuildInputPath(second, current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(
                    parent,
                    current,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return current;
            }
            current = NormalizeBuildInputPathRoot(parent);
        }
        return current;
    }

    /// <summary>
    /// Normalizes a build-input root without trimming a filesystem root into an empty or drive-relative path.
    /// </summary>
    internal static string NormalizeBuildInputPathRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathRoot = Path.GetPathRoot(fullPath)!;
        string trimmedPathRoot = pathRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ||
               string.Equals(
                   trimmed,
                   trimmedPathRoot,
                   IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? pathRoot
            : trimmed;
    }

    private static bool IsReparsePointPath(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void AddPropertyPath(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return;
        }

        string value = property.GetString()!;
        values.Add(Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value)));
    }

    private static bool IsTrustedExternalBuildInfrastructurePath(string path)
        => IsTrustedExternalBuildInfrastructurePath(path, Array.Empty<string>());

    private static bool IsTrustedExternalBuildInfrastructurePath(
        string path,
        IEnumerable<string>? evaluatedRoots)
    {
        string fullPath = Path.GetFullPath(path);
        var roots = new HashSet<string>(
            evaluatedRoots ?? Array.Empty<string>(),
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        AddEnvironmentDirectory(roots, "DOTNET_ROOT");
        AddEnvironmentDirectory(roots, "DOTNET_ROOT(x86)");
        AddEnvironmentDirectory(roots, "MSBuildSDKsPath");
        AddEnvironmentDirectory(roots, "MSBuildExtensionsPath");
        AddEnvironmentDirectory(roots, "NUGET_PACKAGES");

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.GetFullPath(Path.Combine(programFiles, "dotnet")));
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            roots.Add(Path.GetFullPath(Path.Combine(programFilesX86, "dotnet")));
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".dotnet")));
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".nuget", "packages")));
        }

        return roots.Any(root => IsSameOrBelowBuildInputPath(fullPath, root));
    }

    private static string[] ReadTrustedBuildInfrastructureRoots(
        JsonElement properties,
        string projectDirectory)
    {
        string? toolsPath = ReadEvaluatedPath(properties, "MSBuildToolsPath", projectDirectory);
        if (string.IsNullOrWhiteSpace(toolsPath))
            return Array.Empty<string>();

        var versionDirectory = new DirectoryInfo(toolsPath!);
        DirectoryInfo? sdkDirectory = versionDirectory.Parent;
        if (sdkDirectory is not null &&
            sdkDirectory.Name.Equals("sdk", StringComparison.OrdinalIgnoreCase) &&
            sdkDirectory.Parent is not null)
        {
            return [sdkDirectory.Parent.FullName];
        }

        return [versionDirectory.FullName];
    }

    private static void AddEnvironmentDirectory(HashSet<string> roots, string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            roots.Add(Path.GetFullPath(value));
    }

    private static bool IsSameOrBelowBuildInputPath(string path, string root)
    {
        string fullRoot = NormalizeBuildInputPathRoot(root);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string separator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                           fullRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return string.Equals(Path.GetFullPath(path), fullRoot, comparison) ||
               Path.GetFullPath(path).StartsWith(fullRoot + separator, comparison);
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(
            string projectPath,
            string? targetFramework,
            string? configuration,
            IReadOnlyDictionary<string, string>? globalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            IReadOnlyCollection<string>? controlledBuildEnvironmentVariableNames = null,
            IReadOnlyCollection<string>? trustedBuildPackages = null,
            bool requiresSdkPackageEvidence = true,
            IReadOnlyDictionary<string, string>? sdkPackageEvidenceGlobalProperties = null)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
            Configuration = configuration;
            GlobalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            ControlledBuildEnvironmentVariableNames = controlledBuildEnvironmentVariableNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            TrustedBuildPackages = trustedBuildPackages?
                .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                .Select(packageId => packageId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            RequiresSdkPackageEvidence = requiresSdkPackageEvidence;
            SdkPackageEvidenceGlobalProperties = new Dictionary<string, string>(
                sdkPackageEvidenceGlobalProperties ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
        internal bool HasExplicitTargetFramework => !string.IsNullOrEmpty(TargetFramework);
        internal string? Configuration { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal IReadOnlyDictionary<string, string?> EnvironmentVariables { get; }
        internal IReadOnlyCollection<string> ControlledBuildEnvironmentVariableNames { get; }
        internal IReadOnlyCollection<string> TrustedBuildPackages { get; }
        internal bool RequiresSdkPackageEvidence { get; }
        internal IReadOnlyDictionary<string, string> SdkPackageEvidenceGlobalProperties { get; }

        internal IReadOnlyDictionary<string, string> ReadEffectiveGlobalProperties()
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in GlobalProperties)
                properties[property.Key] = property.Value;
            if (!string.IsNullOrWhiteSpace(Configuration))
                properties["Configuration"] = Configuration!;
            if (!string.IsNullOrWhiteSpace(TargetFramework))
                properties["TargetFramework"] = TargetFramework!;
            return properties;
        }

        internal IReadOnlyDictionary<string, string> ReadSdkPackageEvidenceGlobalProperties()
        {
            var properties = new Dictionary<string, string>(
                ReadEffectiveGlobalProperties(),
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in SdkPackageEvidenceGlobalProperties)
                properties[property.Key] = property.Value;
            return properties;
        }

        internal IReadOnlyDictionary<string, string> BuildControlledEvaluationProperties(
            IReadOnlyDictionary<string, string> evaluatedProperties)
            => CreateControlledEvaluationProperties(
                ReadEffectiveGlobalProperties(),
                evaluatedProperties,
                EnvironmentVariables.Keys,
                ControlledBuildEnvironmentVariableNames);

        internal ProjectEvaluationRequest ForProject(string projectPath, string? targetFramework)
            => new(
                Path.GetFullPath(projectPath),
                targetFramework,
                Configuration,
                GlobalProperties,
                EnvironmentVariables,
                ControlledBuildEnvironmentVariableNames,
                TrustedBuildPackages,
                RequiresSdkPackageEvidence,
                SdkPackageEvidenceGlobalProperties);

        internal ProjectEvaluationRequest ForProject(EvaluatedProjectReference projectReference)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in GlobalProperties)
                properties[property.Key] = property.Value;
            foreach (KeyValuePair<string, string> property in projectReference.GlobalProperties)
                properties[property.Key] = property.Value;
            foreach (string propertyName in projectReference.UndefineProperties)
                properties.Remove(propertyName);

            bool undefinesConfiguration = projectReference.UndefineProperties.Contains(
                "Configuration",
                StringComparer.OrdinalIgnoreCase);
            bool undefinesTargetFramework = projectReference.UndefineProperties.Contains(
                "TargetFramework",
                StringComparer.OrdinalIgnoreCase);
            string? configuration = undefinesConfiguration
                ? null
                : properties.TryGetValue("Configuration", out string? childConfiguration)
                    ? childConfiguration
                    : Configuration;
            string? targetFramework = undefinesTargetFramework
                ? null
                : projectReference.TargetFramework ??
                  (string.IsNullOrWhiteSpace(TargetFramework)
                      ? null
                      : ResolveNearestDeclaredTargetFrameworkUnconditionally(
                          projectReference.ProjectPath,
                          TargetFramework!));
            properties.Remove("Configuration");
            properties.Remove("TargetFramework");
            return new ProjectEvaluationRequest(
                Path.GetFullPath(projectReference.ProjectPath),
                targetFramework,
                configuration,
                properties,
                EnvironmentVariables,
                ControlledBuildEnvironmentVariableNames,
                TrustedBuildPackages,
                requiresSdkPackageEvidence: false);
        }

        internal string BuildVisitKey()
        {
            var key = new System.Text.StringBuilder();
            AppendProjectReferenceKeySegment(key, "ProjectPath");
            AppendProjectReferenceKeySegment(key, NormalizeProjectReferenceIdentityPath(ProjectPath));
            AppendProjectReferenceKeySegment(key, "TargetFramework");
            AppendProjectReferenceKeySegment(key, TargetFramework is null ? "Undefined" : "Defined");
            AppendProjectReferenceKeySegment(key, TargetFramework ?? string.Empty);
            AppendProjectReferenceKeySegment(key, "Configuration");
            AppendProjectReferenceKeySegment(key, Configuration is null ? "Undefined" : "Defined");
            AppendProjectReferenceKeySegment(key, Configuration ?? string.Empty);
            foreach (KeyValuePair<string, string> property in GlobalProperties.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                AppendProjectReferenceKeySegment(key, "Property");
                AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(property.Key));
                AppendProjectReferenceKeySegment(key, HashProjectEvaluationIdentityValue(property.Value));
            }
            foreach (KeyValuePair<string, string> property in SdkPackageEvidenceGlobalProperties.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                AppendProjectReferenceKeySegment(key, "SdkEvidenceProperty");
                AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(property.Key));
                AppendProjectReferenceKeySegment(key, HashProjectEvaluationIdentityValue(property.Value));
            }
            foreach (KeyValuePair<string, string?> environmentVariable in EnvironmentVariables.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                AppendProjectReferenceKeySegment(key, "Environment");
                AppendProjectReferenceKeySegment(key, NormalizeEnvironmentIdentityName(environmentVariable.Key));
                AppendProjectReferenceKeySegment(
                    key,
                    HashProjectEvaluationIdentityValue(environmentVariable.Value ?? string.Empty));
            }
            foreach (string name in ControlledBuildEnvironmentVariableNames)
            {
                AppendProjectReferenceKeySegment(key, "ControlledEnvironment");
                AppendProjectReferenceKeySegment(key, NormalizeEnvironmentIdentityName(name));
            }
            AppendProjectReferenceKeySegment(key, "SdkPackageEvidence");
            AppendProjectReferenceKeySegment(key, RequiresSdkPackageEvidence ? "Required" : "Inherited");
            return key.ToString();
        }
    }

    internal static string HashProjectEvaluationIdentityValue(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty);
    }

    private sealed class EvaluatedProjectInputs
    {
        internal EvaluatedProjectInputs(
            string[] buildInputs,
            string[] msBuildInputs,
            string[] sourceInputs,
            EvaluatedProjectReference[] projectReferences,
            string[] targetFrameworks,
            string[] outputRoots,
            string[] expectedOutputPaths,
            string? intermediateRoot,
            string? intermediateOutputPath,
            string? pathMap,
            GeneratedProjectReferenceOutput[] generatedProjectReferenceOutputs,
            EvaluatedPublishInput[] publishInputs,
            VerifiedPackageInputCatalog? verifiedPackages,
            string[] trustedBuildInfrastructureRoots,
            IReadOnlyDictionary<string, string> evaluatedProperties,
            string? customAfterMicrosoftCommonTargets,
            string? packageId,
            string? packageVersion,
            string? packageValidationBaselineVersion)
        {
            BuildInputs = buildInputs;
            MsBuildInputs = msBuildInputs;
            SourceInputs = sourceInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
            OutputRoots = outputRoots;
            ExpectedOutputPaths = expectedOutputPaths;
            IntermediateRoot = intermediateRoot;
            IntermediateOutputPath = intermediateOutputPath;
            PathMap = pathMap;
            GeneratedProjectReferenceOutputs = generatedProjectReferenceOutputs;
            PublishInputs = publishInputs;
            VerifiedPackages = verifiedPackages;
            TrustedBuildInfrastructureRoots = trustedBuildInfrastructureRoots;
            EvaluatedProperties = evaluatedProperties;
            CustomAfterMicrosoftCommonTargets = customAfterMicrosoftCommonTargets;
            PackageId = packageId;
            PackageVersion = packageVersion;
            PackageValidationBaselineVersion = packageValidationBaselineVersion;
        }

        internal string[] BuildInputs { get; }
        internal string[] MsBuildInputs { get; }
        internal string[] SourceInputs { get; }
        internal EvaluatedProjectReference[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
        internal string[] OutputRoots { get; }
        internal string[] ExpectedOutputPaths { get; }
        internal string? IntermediateRoot { get; }
        internal string? IntermediateOutputPath { get; }
        internal string? PathMap { get; }
        internal GeneratedProjectReferenceOutput[] GeneratedProjectReferenceOutputs { get; }
        internal EvaluatedPublishInput[] PublishInputs { get; }
        internal VerifiedPackageInputCatalog? VerifiedPackages { get; }
        internal string[] TrustedBuildInfrastructureRoots { get; }
        internal IReadOnlyDictionary<string, string> EvaluatedProperties { get; }
        internal string? CustomAfterMicrosoftCommonTargets { get; }
        internal string? PackageId { get; }
        internal string? PackageVersion { get; }
        internal string? PackageValidationBaselineVersion { get; }
    }

    private sealed class EvaluatedProjectReference
    {
        internal EvaluatedProjectReference(
            string projectPath,
            string? targetFramework,
            IReadOnlyDictionary<string, string>? globalProperties = null,
            string[]? undefineProperties = null)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
            GlobalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UndefineProperties = undefineProperties ?? Array.Empty<string>();
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal string[] UndefineProperties { get; }
    }

    private sealed class GeneratedProjectReferenceOutput
    {
        internal GeneratedProjectReferenceOutput(string outputPath, EvaluatedProjectReference projectReference)
        {
            OutputPath = outputPath;
            ProjectReference = projectReference;
        }

        internal string OutputPath { get; }
        internal EvaluatedProjectReference ProjectReference { get; }
    }
}
