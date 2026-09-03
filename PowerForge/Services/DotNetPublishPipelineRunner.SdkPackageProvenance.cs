using System.Text.Json;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const string SdkEvidenceNuGetOrgSource = "https://api.nuget.org/v3/index.json";

    private static readonly HashSet<string> SdkEvidenceOwnedProperties = new(
        new[]
        {
            "MSBuildProjectExtensionsPath",
            "NuGetLockFilePath",
            "RestoreAdditionalProjectFallbackFolders",
            "RestoreAdditionalProjectSources",
            "RestoreConfigFile",
            "RestoreFallbackFolders",
            "RestoreGraphOutputPath",
            "RestoreIgnoreFailedSources",
            "RestoreLockedMode",
            "RestoreNoCache",
            "RestoreOutputPath",
            "RestorePackagesPath",
            "RestorePackagesWithLockFile",
            "RestoreRecursive",
            "RestoreRepositoryPath",
            "RestoreSources"
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TrustedSdkAutoReferencedPackageIds = new(
        new[]
        {
            "Microsoft.NET.ILLink.Tasks"
        },
        StringComparer.OrdinalIgnoreCase);

    private static string? AddSdkManagedPackageHashes(
        string projectPath,
        JsonElement properties,
        ICollection<string> packageRoots,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        IReadOnlyDictionary<string, string> committedArchivePaths,
        VerifiedPackageArchiveCache archives,
        out string? failureReason)
    {
        failureReason = null;
        if (!TryReadTrustedSdkRestoreGraph(
                projectPath,
                properties,
                effectiveGlobalProperties,
                environmentVariables,
                committedArchivePaths,
                archives,
                out JsonDocument? document,
                out string? evidenceRoot,
                out string? isolatedPackageRoot,
                out failureReason) ||
            document is null)
        {
            return null;
        }

        using (document)
        {
            if (!TryReadRestoreGraphProject(document.RootElement, projectPath, out JsonElement project) ||
                !project.TryGetProperty("frameworks", out JsonElement frameworks) ||
                frameworks.ValueKind != JsonValueKind.Object)
            {
                TryDeleteSdkEvidenceRoot(evidenceRoot);
                return null;
            }

            var downloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty framework in frameworks.EnumerateObject())
            {
                if (framework.Value.TryGetProperty("dependencies", out JsonElement dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty dependency in dependencies.EnumerateObject())
                        AddTrustedAutoReferencedPackageKey(
                            dependency,
                            isolatedPackageRoot!,
                            hashes,
                            sdkManagedPackageKeys);
                }

                AddSdkDownloadDependencies(framework.Value, downloads);
            }

            foreach (string download in downloads)
                AddSdkDownloadPackageHash(download, new[] { isolatedPackageRoot! }, hashes);

            packageRoots.Add(isolatedPackageRoot!);
            return evidenceRoot;
        }
    }

    private static bool TryReadTrustedSdkRestoreGraph(
        string projectPath,
        JsonElement properties,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        IReadOnlyDictionary<string, string> committedArchivePaths,
        VerifiedPackageArchiveCache archives,
        out JsonDocument? document,
        out string? evidenceRoot,
        out string? isolatedPackageRoot,
        out string? failureReason)
    {
        document = null;
        evidenceRoot = null;
        isolatedPackageRoot = null;
        failureReason = null;
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "pf-rg-" + Guid.NewGuid().ToString("N"));
        try
        {
            string intermediateRoot = Directory.CreateDirectory(
                Path.Combine(temporaryRoot, "obj")).FullName + Path.DirectorySeparatorChar;
            isolatedPackageRoot = Directory.CreateDirectory(
                Path.Combine(temporaryRoot, "packages")).FullName;
            string verifiedPackageSource = Directory.CreateDirectory(
                Path.Combine(temporaryRoot, "verified-lock")).FullName;
            if (!archives.TrySeedVerifiedRestoreSource(verifiedPackageSource, committedArchivePaths))
            {
                failureReason = "verified-lock source seeding failed";
                return false;
            }
            string graphPath = Path.Combine(temporaryRoot, "restore-graph.json");
            string lockPath = Path.Combine(temporaryRoot, "restore.lock.json");
            string configPath = Path.Combine(temporaryRoot, "NuGet.Config");
            if (!TryWriteSdkEvidenceNuGetConfig(
                    configPath,
                    verifiedPackageSource,
                    usePublicSource: false))
            {
                failureReason = "offline NuGet configuration could not be written";
                return false;
            }
            var graphArguments = new List<string>
            {
                "msbuild",
                projectPath,
                "-nologo",
                "-maxCpuCount:1",
                "-nodeReuse:false",
                "-verbosity:quiet",
                "-noAutoResponse",
                "-target:GenerateRestoreGraphFile",
                "-p:RestoreGraphOutputPath=" + EscapeMsBuildPropertyValue(graphPath)
            };
            AppendSdkEvidenceProperties(graphArguments, properties, effectiveGlobalProperties);
            AppendSdkEvidenceOwnedProperties(
                graphArguments,
                intermediateRoot,
                configPath,
                verifiedPackageSource);

            var graphProcess = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                graphArguments,
                environmentVariables,
                TimeSpan.FromMinutes(2));
            if (graphProcess.ExitCode != 0 || graphProcess.TimedOut || !File.Exists(graphPath))
            {
                failureReason = graphProcess.TimedOut
                    ? "restore graph generation timed out"
                    : $"restore graph generation failed with exit code {graphProcess.ExitCode}";
                return false;
            }

            document = JsonDocument.Parse(File.ReadAllText(graphPath));
            if (!TryReadSdkEvidencePackageKeys(
                    document.RootElement,
                    projectPath,
                    out HashSet<string> sdkPackageKeys))
            {
                failureReason = "SDK package keys could not be read from the restore graph";
                return false;
            }
            if (!TryPrimeSdkEvidencePackages(
                    temporaryRoot,
                    isolatedPackageRoot,
                    verifiedPackageSource,
                    configPath,
                    committedArchivePaths.Keys,
                    "verified-lock",
                    Path.GetDirectoryName(projectPath)!,
                    environmentVariables))
            {
                failureReason = "locked packages could not be primed into the isolated package root";
                return false;
            }
            if (!TryWriteSdkEvidenceNuGetConfig(
                    configPath,
                    verifiedPackageSource,
                    usePublicSource: true))
            {
                failureReason = "online SDK-evidence NuGet configuration could not be written";
                return false;
            }
            if (!TryPrimeSdkEvidencePackages(
                    temporaryRoot,
                    isolatedPackageRoot,
                    SdkEvidenceNuGetOrgSource,
                    configPath,
                    sdkPackageKeys,
                    "sdk-packages",
                    Path.GetDirectoryName(projectPath)!,
                    environmentVariables))
            {
                failureReason = "SDK-owned packages could not be primed from the trusted source";
                return false;
            }
            if (!TryReadSdkPackageHashes(
                    isolatedPackageRoot,
                    sdkPackageKeys,
                    out Dictionary<string, string> trustedSdkPackageHashes) ||
                !TrySnapshotSdkPackageArchives(
                    isolatedPackageRoot,
                    trustedSdkPackageHashes,
                    archives))
            {
                failureReason = "SDK package hashes or archive snapshots could not be verified";
                return false;
            }
            if (!TryWriteSdkEvidenceNuGetConfig(
                    configPath,
                    verifiedPackageSource,
                    usePublicSource: false))
            {
                failureReason = "final offline NuGet configuration could not be written";
                return false;
            }

            var restoreArguments = new List<string>
            {
                "restore",
                projectPath,
                "--force-evaluate",
                "--no-cache",
                "--nologo",
                "--packages",
                isolatedPackageRoot,
                "--configfile",
                configPath
            };
            AppendSdkEvidenceProperties(restoreArguments, properties, effectiveGlobalProperties);
            AppendSdkEvidenceOwnedProperties(
                restoreArguments,
                intermediateRoot,
                configPath,
                verifiedPackageSource);
            restoreArguments.Add("-p:RestorePackagesWithLockFile=true");
            restoreArguments.Add("-p:RestoreLockedMode=false");
            restoreArguments.Add("-p:NuGetLockFilePath=" + EscapeMsBuildPropertyValue(lockPath));
            restoreArguments.Add("-p:NuGetAudit=false");
            var restoreProcess = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                restoreArguments,
                environmentVariables,
                TimeSpan.FromMinutes(5));
            if (restoreProcess.ExitCode != 0 || restoreProcess.TimedOut)
            {
                failureReason = restoreProcess.TimedOut
                    ? "isolated SDK-evidence restore timed out"
                    : $"isolated SDK-evidence restore failed with exit code {restoreProcess.ExitCode}";
                return false;
            }
            if (!TryReadSdkPackageHashes(
                    isolatedPackageRoot,
                    sdkPackageKeys,
                    out Dictionary<string, string> postRestoreSdkPackageHashes) ||
                !HaveSamePackageHashes(trustedSdkPackageHashes, postRestoreSdkPackageHashes) ||
                !TryVerifyCurrentSdkPackageArchives(
                    isolatedPackageRoot,
                    trustedSdkPackageHashes))
            {
                failureReason = "post-restore SDK package verification failed";
                return false;
            }

            evidenceRoot = temporaryRoot;
            return true;
        }
        catch (Exception exception)
        {
            document?.Dispose();
            document = null;
            failureReason = $"{exception.GetType().Name} while collecting SDK package evidence";
            return false;
        }
        finally
        {
            if (evidenceRoot is null)
            {
                document?.Dispose();
                document = null;
                TryDeleteSdkEvidenceRoot(temporaryRoot);
            }
        }
    }

    internal static void AppendSdkEvidenceProperties(
        ICollection<string> arguments,
        JsonElement properties,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string propertyName in new[]
                 {
                     "Configuration",
                     "TargetFramework",
                     "RuntimeIdentifier",
                     "RuntimeIdentifiers",
                     "SelfContained",
                     "UseAppHost",
                     "UseWPF",
                     "UseWindowsForms",
                     "PublishSingleFile",
                     "PublishTrimmed",
                     "PublishAot",
                     "PublishReadyToRun"
                 })
        {
            string? value = effectiveGlobalProperties is not null &&
                            effectiveGlobalProperties.TryGetValue(propertyName, out string? globalValue)
                ? globalValue
                : properties.TryGetProperty(propertyName, out JsonElement evaluatedValue) &&
                  evaluatedValue.ValueKind == JsonValueKind.String
                    ? evaluatedValue.GetString()
                    : null;
            if (!string.IsNullOrWhiteSpace(value))
                values[propertyName] = value!;
        }

        foreach (KeyValuePair<string, string> property in effectiveGlobalProperties ??
                 new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(property.Key) &&
                !SdkEvidenceOwnedProperties.Contains(property.Key) &&
                IsSafeSdkEvidencePropertyName(property.Key))
            {
                values[property.Key] = property.Value ?? string.Empty;
            }
        }

        foreach (KeyValuePair<string, string> property in values.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("RuntimeIdentifiers", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("-p:RuntimeIdentifiers=" + BuildMsBuildListPropertyValue(
                    property.Value.Split(
                        new[] { ';' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)));
                continue;
            }

            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
    }

    private static void AppendSdkEvidenceOwnedProperties(
        ICollection<string> arguments,
        string intermediateRoot,
        string configPath,
        string restoreSource)
    {
        arguments.Add("-p:MSBuildProjectExtensionsPath=" + EscapeMsBuildPropertyValue(intermediateRoot));
        arguments.Add("-p:RestoreConfigFile=" + EscapeMsBuildPropertyValue(configPath));
        arguments.Add("-p:RestoreOutputPath=" + EscapeMsBuildPropertyValue(intermediateRoot));
        arguments.Add("-p:RestoreSources=" + EscapeMsBuildPropertyValue(restoreSource));
        arguments.Add("-p:RestoreFallbackFolders=");
        arguments.Add("-p:RestoreAdditionalProjectFallbackFolders=");
        arguments.Add("-p:RestoreAdditionalProjectSources=");
        arguments.Add("-p:RestoreRecursive=false");
    }

    private static bool IsSafeSdkEvidencePropertyName(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
            return false;
        return name.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
    }

    private static bool TryReadSdkEvidencePackageKeys(
        JsonElement root,
        string projectPath,
        out HashSet<string> packageKeys)
    {
        packageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryReadRestoreGraphProject(root, projectPath, out JsonElement project) ||
            !project.TryGetProperty("frameworks", out JsonElement frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty framework in frameworks.EnumerateObject())
        {
            if (framework.Value.TryGetProperty("dependencies", out JsonElement dependencies) &&
                dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.TryGetProperty("autoReferenced", out JsonElement autoReferenced) &&
                        autoReferenced.ValueKind == JsonValueKind.True)
                    {
                        if (!IsTrustedSdkAutoReferencedPackageId(dependency.Name))
                            continue;
                        if (!TryAddSdkEvidencePackageKey(
                                dependency.Name,
                                dependency.Value,
                                packageKeys))
                        {
                            return false;
                        }
                    }
                }
            }

            if (!framework.Value.TryGetProperty("downloadDependencies", out JsonElement downloads) ||
                downloads.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (JsonElement download in downloads.EnumerateArray())
            {
                if (!download.TryGetProperty("name", out JsonElement name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()) ||
                    !TryAddSdkEvidencePackageKey(name.GetString()!, download, packageKeys))
                {
                    return false;
                }
            }
        }
        return packageKeys.Count > 0;
    }

    private static bool TryAddSdkEvidencePackageKey(
        string packageId,
        JsonElement package,
        ISet<string> packageKeys)
    {
        if (!package.TryGetProperty("version", out JsonElement versionElement) ||
            versionElement.ValueKind != JsonValueKind.String ||
            !VersionRange.TryParse(versionElement.GetString()!, out VersionRange? range) ||
            range.MinVersion is null ||
            !range.IsMinInclusive)
        {
            return false;
        }

        packageKeys.Add(packageId + "|" + range.MinVersion.ToNormalizedString());
        return true;
    }

    private static bool IsTrustedSdkAutoReferencedPackageId(string packageId)
        => TrustedSdkAutoReferencedPackageIds.Contains(packageId);

    private static bool TryWriteSdkEvidenceNuGetConfig(
        string configPath,
        string verifiedPackageSource,
        bool usePublicSource)
    {
        try
        {
            string source = usePublicSource ? SdkEvidenceNuGetOrgSource : verifiedPackageSource;
            var packageSources = new XElement(
                "packageSources",
                new XElement("clear"),
                new XElement(
                    "add",
                    new XAttribute("key", usePublicSource ? "nuget.org" : "verified-lock"),
                    new XAttribute("value", source),
                    usePublicSource ? new XAttribute("protocolVersion", "3") : null));
            var root = new XElement(
                "configuration",
                packageSources,
                new XElement("fallbackPackageFolders", new XElement("clear")),
                new XElement("disabledPackageSources", new XElement("clear")));
            new XDocument(root).Save(configPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPrimeSdkEvidencePackages(
        string temporaryRoot,
        string isolatedPackageRoot,
        string restoreSource,
        string configPath,
        IEnumerable<string> packageKeys,
        string projectName,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        try
        {
            var packages = packageKeys
                .Select(key =>
                {
                    int separator = key.LastIndexOf('|');
                    return separator > 0 && separator < key.Length - 1
                        ? (Id: key.Substring(0, separator), Version: key.Substring(separator + 1))
                        : default;
                })
                .Where(package => !string.IsNullOrWhiteSpace(package.Id) &&
                                  !string.IsNullOrWhiteSpace(package.Version))
                .Distinct()
                .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packages.Length == 0)
                return true;

            string projectPath = Path.Combine(temporaryRoot, projectName + ".csproj");
            string intermediateRoot = Directory.CreateDirectory(
                Path.Combine(temporaryRoot, projectName + "-obj")).FullName + Path.DirectorySeparatorChar;
            new XDocument(
                new XElement(
                    "Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement(
                        "PropertyGroup",
                        new XElement("TargetFramework", "net8.0")),
                    new XElement(
                        "ItemGroup",
                        packages.Select(package => new XElement(
                            "PackageDownload",
                            new XAttribute("Include", package.Id),
                            new XAttribute("Version", "[" + package.Version + "]"))))))
                .Save(projectPath);

            var arguments = new List<string>
            {
                "restore",
                projectPath,
                "--force-evaluate",
                "--no-cache",
                "--nologo",
                "--packages",
                isolatedPackageRoot,
                "--configfile",
                configPath
            };
            AppendSdkEvidenceOwnedProperties(
                arguments,
                intermediateRoot,
                configPath,
                restoreSource);
            AppendSyntheticSdkEvidenceProjectIsolationProperties(arguments);
            arguments.Add("-p:NuGetAudit=false");
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                workingDirectory,
                arguments,
                environmentVariables,
                TimeSpan.FromMinutes(2));
            return process.ExitCode == 0 && !process.TimedOut;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendSyntheticSdkEvidenceProjectIsolationProperties(
        ICollection<string> arguments)
    {
        arguments.Add("-p:DisableImplicitFrameworkReferences=true");
        arguments.Add("-p:ImportDirectoryBuildProps=false");
        arguments.Add("-p:ImportDirectoryBuildTargets=false");
        arguments.Add("-p:ImportDirectoryPackagesProps=false");
    }

    private static bool TryReadSdkPackageHashes(
        string isolatedPackageRoot,
        IEnumerable<string> packageKeys,
        out Dictionary<string, string> hashes)
    {
        hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] expectedKeys = packageKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string packageKey in expectedKeys)
            AddSdkDownloadPackageHash(packageKey, new[] { isolatedPackageRoot }, hashes);

        if (hashes.Count != expectedKeys.Length)
            return false;
        foreach (string key in expectedKeys)
        {
            if (!hashes.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                return false;
        }
        return true;
    }

    private static bool TrySnapshotSdkPackageArchives(
        string isolatedPackageRoot,
        IReadOnlyDictionary<string, string> hashes,
        VerifiedPackageArchiveCache archives)
    {
        foreach (KeyValuePair<string, string> package in hashes)
        {
            if (!TryGetSdkPackageArchivePath(isolatedPackageRoot, package.Key, out string? archivePath) ||
                archives.TryGetOrOpen(archivePath!, package.Value) is null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryVerifyCurrentSdkPackageArchives(
        string isolatedPackageRoot,
        IReadOnlyDictionary<string, string> hashes)
    {
        foreach (KeyValuePair<string, string> package in hashes)
        {
            if (!TryGetSdkPackageArchivePath(isolatedPackageRoot, package.Key, out string? archivePath))
                return false;
            using VerifiedPackageArchive? archive = VerifiedPackageArchive.TryOpen(archivePath!, package.Value);
            if (archive is null)
                return false;
        }
        return true;
    }

    private static bool TryGetSdkPackageArchivePath(
        string isolatedPackageRoot,
        string packageKey,
        out string? archivePath)
    {
        archivePath = null;
        int separator = packageKey.LastIndexOf('|');
        if (separator <= 0 || separator == packageKey.Length - 1)
            return false;

        string packageId = packageKey.Substring(0, separator).ToLowerInvariant();
        string packageVersion = packageKey.Substring(separator + 1).ToLowerInvariant();
        string candidate = Path.Combine(
            Path.GetFullPath(isolatedPackageRoot),
            packageId,
            packageVersion,
            packageId + "." + packageVersion + ".nupkg");
        if (!File.Exists(candidate) || HasReparsePointBelowRoot(candidate, isolatedPackageRoot))
            return false;

        archivePath = candidate;
        return true;
    }

    private static bool HaveSamePackageHashes(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        return expected.Count == actual.Count &&
               expected.All(package => actual.TryGetValue(package.Key, out string? hash) &&
                                       string.Equals(package.Value, hash, StringComparison.Ordinal));
    }

    private static void TryDeleteSdkEvidenceRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string fullPath = Path.GetFullPath(path!);
            string tempPath = Path.GetFullPath(Path.GetTempPath());
            if (IsSameOrBelowBuildInputPath(fullPath, tempPath) &&
                Path.GetFileName(fullPath).StartsWith("pf-rg-", StringComparison.Ordinal))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch
        {
            // Temporary SDK evidence cleanup is best effort only.
        }
    }

    private static bool TryReadRestoreGraphProject(
        JsonElement root,
        string projectPath,
        out JsonElement project)
    {
        project = default;
        if (!root.TryGetProperty("projects", out JsonElement projects) ||
            projects.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string fullProjectPath = Path.GetFullPath(projectPath);
        foreach (JsonProperty candidate in projects.EnumerateObject())
        {
            string candidatePath;
            try
            {
                candidatePath = Path.GetFullPath(candidate.Name);
            }
            catch
            {
                continue;
            }
            if (!candidatePath.Equals(
                    fullProjectPath,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                continue;
            }
            project = candidate.Value;
            return true;
        }
        return false;
    }

    private static void AddTrustedAutoReferencedPackageKey(
        JsonProperty dependency,
        string isolatedPackageRoot,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        if (!dependency.Value.TryGetProperty("autoReferenced", out JsonElement autoReferenced) ||
            autoReferenced.ValueKind != JsonValueKind.True ||
            !IsTrustedSdkAutoReferencedPackageId(dependency.Name) ||
            !dependency.Value.TryGetProperty("version", out JsonElement version) ||
            version.ValueKind != JsonValueKind.String ||
            !VersionRange.TryParse(version.GetString()!, out VersionRange? range))
        {
            return;
        }

        string packageDirectory = Path.Combine(
            isolatedPackageRoot,
            dependency.Name.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory) || HasReparsePointBelowRoot(packageDirectory, isolatedPackageRoot))
            return;
        string[] candidates = Directory.EnumerateDirectories(packageDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => NuGetVersion.TryParse(Path.GetFileName(path), out NuGetVersion? version) &&
                           range.Satisfies(version))
            .ToArray();
        if (candidates.Length != 1)
            return;
        string resolvedVersion = Path.GetFileName(candidates[0]);
        string packageKey = dependency.Name + "|" + resolvedVersion;
        AddSdkDownloadPackageHash(packageKey, new[] { isolatedPackageRoot }, hashes);
        if (hashes.TryGetValue(packageKey, out string? contentHash) &&
            !string.IsNullOrWhiteSpace(contentHash))
        {
            sdkManagedPackageKeys.Add(packageKey);
        }
    }

    private static void AddSdkDownloadDependencies(JsonElement framework, HashSet<string> downloads)
    {
        if (!framework.TryGetProperty("downloadDependencies", out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement download in values.EnumerateArray())
        {
            if (!download.TryGetProperty("name", out JsonElement name) ||
                name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) ||
                !download.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(version.GetString()) ||
                !VersionRange.TryParse(version.GetString()!, out VersionRange? range) ||
                range.MinVersion is null ||
                range.MaxVersion is null ||
                !range.IsMinInclusive ||
                !range.IsMaxInclusive ||
                !range.MinVersion.Equals(range.MaxVersion))
            {
                continue;
            }

            downloads.Add(name.GetString()! + "|" + range.MinVersion.ToNormalizedString());
        }
    }

    private static void AddSdkDownloadPackageHash(
        string packageKey,
        IEnumerable<string> packageRoots,
        Dictionary<string, string> hashes)
    {
        string[] parts = packageKey.Split('|');
        if (parts.Length != 2)
            return;

        string? discoveredHash = null;
        foreach (string root in packageRoots)
        {
            string metadataPath = Path.Combine(
                Path.GetFullPath(root),
                parts[0].ToLowerInvariant(),
                parts[1].ToLowerInvariant(),
                ".nupkg.metadata");
            if (!File.Exists(metadataPath) || HasReparsePointBelowRoot(metadataPath, root))
                continue;

            try
            {
                // NuGet records the restore digest here. The package catalog later rechecks both
                // the archive content hash and the extracted input against that archive.
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (!metadata.RootElement.TryGetProperty("contentHash", out JsonElement contentHash) ||
                    contentHash.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(contentHash.GetString()))
                {
                    continue;
                }

                string value = contentHash.GetString()!;
                if (discoveredHash is not null &&
                    !string.Equals(discoveredHash, value, StringComparison.Ordinal))
                {
                    hashes[packageKey] = string.Empty;
                    return;
                }
                discoveredHash = value;
            }
            catch
            {
                hashes[packageKey] = string.Empty;
                return;
            }
        }

        AddPackageHash(packageKey, discoveredHash, hashes);
    }

    private static void AddPackageHash(
        string packageKey,
        string? value,
        Dictionary<string, string> hashes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (hashes.TryGetValue(packageKey, out string? existing) &&
            !string.Equals(existing, value, StringComparison.Ordinal))
        {
            hashes[packageKey] = string.Empty;
        }
        else
        {
            hashes[packageKey] = value!;
        }
    }
}
