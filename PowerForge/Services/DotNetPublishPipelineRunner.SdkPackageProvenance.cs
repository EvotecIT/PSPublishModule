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

    private static string? AddSdkManagedPackageHashes(
        string projectPath,
        JsonElement properties,
        ICollection<string> packageRoots,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        IReadOnlyDictionary<string, string> committedArchivePaths,
        VerifiedPackageArchiveCache archives)
    {
        if (!TryReadTrustedSdkRestoreGraph(
                projectPath,
                properties,
                effectiveGlobalProperties,
                environmentVariables,
                committedArchivePaths,
                archives,
                out JsonDocument? document,
                out string? evidenceRoot,
                out string? isolatedPackageRoot) ||
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
        out string? isolatedPackageRoot)
    {
        document = null;
        evidenceRoot = null;
        isolatedPackageRoot = null;
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
                return false;
            string graphPath = Path.Combine(temporaryRoot, "restore-graph.json");
            string lockPath = Path.Combine(temporaryRoot, "restore.lock.json");
            string configPath = Path.Combine(temporaryRoot, "NuGet.Config");
            if (!TryWriteSdkEvidenceNuGetConfig(
                    configPath,
                    verifiedPackageSource,
                    committedArchivePaths.Keys,
                    Array.Empty<string>()))
            {
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
            AppendSdkEvidenceOwnedProperties(graphArguments, intermediateRoot, configPath);

            var graphProcess = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                graphArguments,
                environmentVariables,
                TimeSpan.FromMinutes(2));
            if (graphProcess.ExitCode != 0 || graphProcess.TimedOut || !File.Exists(graphPath))
            {
                return false;
            }

            document = JsonDocument.Parse(File.ReadAllText(graphPath));
            if (!TryReadSdkEvidencePackageIds(
                    document.RootElement,
                    projectPath,
                    out HashSet<string> sdkPackageIds) ||
                !TryWriteSdkEvidenceNuGetConfig(
                    configPath,
                    verifiedPackageSource,
                    committedArchivePaths.Keys,
                    sdkPackageIds))
            {
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
            AppendSdkEvidenceOwnedProperties(restoreArguments, intermediateRoot, configPath);
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
                return false;
            }

            evidenceRoot = temporaryRoot;
            return true;
        }
        catch
        {
            document?.Dispose();
            document = null;
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

    private static void AppendSdkEvidenceProperties(
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
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
    }

    private static void AppendSdkEvidenceOwnedProperties(
        ICollection<string> arguments,
        string intermediateRoot,
        string configPath)
    {
        arguments.Add("-p:MSBuildProjectExtensionsPath=" + EscapeMsBuildPropertyValue(intermediateRoot));
        arguments.Add("-p:RestoreConfigFile=" + EscapeMsBuildPropertyValue(configPath));
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

    private static bool TryReadSdkEvidencePackageIds(
        JsonElement root,
        string projectPath,
        out HashSet<string> packageIds)
    {
        packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        packageIds.Add(dependency.Name);
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
                if (download.TryGetProperty("name", out JsonElement name) &&
                    name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    packageIds.Add(name.GetString()!);
                }
            }
        }
        return true;
    }

    private static bool TryWriteSdkEvidenceNuGetConfig(
        string configPath,
        string verifiedPackageSource,
        IEnumerable<string> committedPackageKeys,
        IEnumerable<string> sdkPackageIds)
    {
        try
        {
            var committedIds = new HashSet<string>(
                committedPackageKeys.Select(key =>
                {
                    int separator = key.LastIndexOf('|');
                    return separator > 0 ? key.Substring(0, separator) : string.Empty;
                }).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var publicSdkIds = new HashSet<string>(
                sdkPackageIds.Where(id => !committedIds.Contains(id)),
                StringComparer.OrdinalIgnoreCase);

            var packageSources = new XElement(
                "packageSources",
                new XElement("clear"),
                new XElement("add", new XAttribute("key", "verified-lock"), new XAttribute("value", verifiedPackageSource)),
                new XElement(
                    "add",
                    new XAttribute("key", "nuget.org"),
                    new XAttribute("value", SdkEvidenceNuGetOrgSource),
                    new XAttribute("protocolVersion", "3")));
            var root = new XElement(
                "configuration",
                packageSources,
                new XElement("fallbackPackageFolders", new XElement("clear")),
                new XElement("disabledPackageSources", new XElement("clear")));

            if (committedIds.Count > 0 || publicSdkIds.Count > 0)
            {
                var mappings = new XElement("packageSourceMapping");
                if (committedIds.Count > 0)
                {
                    mappings.Add(new XElement(
                        "packageSource",
                        new XAttribute("key", "verified-lock"),
                        committedIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                            .Select(id => new XElement("package", new XAttribute("pattern", id)))));
                }
                if (publicSdkIds.Count > 0)
                {
                    mappings.Add(new XElement(
                        "packageSource",
                        new XAttribute("key", "nuget.org"),
                        publicSdkIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                            .Select(id => new XElement("package", new XAttribute("pattern", id)))));
                }
                root.Add(mappings);
            }

            new XDocument(root).Save(configPath);
            return true;
        }
        catch
        {
            return false;
        }
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
