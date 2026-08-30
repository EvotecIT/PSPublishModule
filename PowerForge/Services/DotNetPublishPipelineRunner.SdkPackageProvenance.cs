using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string? AddSdkManagedPackageHashes(
        string projectPath,
        JsonElement properties,
        ICollection<string> packageRoots,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties)
    {
        if (!TryReadTrustedSdkRestoreGraph(
                projectPath,
                properties,
                effectiveGlobalProperties,
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
            string graphPath = Path.Combine(temporaryRoot, "restore-graph.json");
            string lockPath = Path.Combine(temporaryRoot, "restore.lock.json");
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
                "-p:RestoreGraphOutputPath=" + EscapeMsBuildPropertyValue(graphPath),
                "-p:MSBuildProjectExtensionsPath=" + EscapeMsBuildPropertyValue(intermediateRoot)
            };
            AppendSdkEvidenceProperties(graphArguments, properties, effectiveGlobalProperties);

            var graphProcess = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                graphArguments,
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
            if (graphProcess.ExitCode != 0 || graphProcess.TimedOut || !File.Exists(graphPath))
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
                "-p:RestorePackagesWithLockFile=true",
                "-p:RestoreLockedMode=false",
                "-p:NuGetLockFilePath=" + EscapeMsBuildPropertyValue(lockPath),
                "-p:MSBuildProjectExtensionsPath=" + EscapeMsBuildPropertyValue(intermediateRoot),
                "-p:NuGetAudit=false"
            };
            AppendSdkEvidenceProperties(restoreArguments, properties, effectiveGlobalProperties);
            var restoreProcess = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                restoreArguments,
                environmentVariables: null,
                TimeSpan.FromMinutes(5));
            if (restoreProcess.ExitCode != 0 || restoreProcess.TimedOut)
            {
                return false;
            }

            document = JsonDocument.Parse(File.ReadAllText(graphPath));
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
                TryDeleteSdkEvidenceRoot(temporaryRoot);
        }
    }

    private static void AppendSdkEvidenceProperties(
        ICollection<string> arguments,
        JsonElement properties,
        IReadOnlyDictionary<string, string>? effectiveGlobalProperties)
    {
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
                arguments.Add("-p:" + propertyName + "=" + EscapeMsBuildPropertyValue(value!));
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
