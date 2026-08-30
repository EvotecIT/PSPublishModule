using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void AddSdkManagedPackageHashes(
        string projectPath,
        JsonElement properties,
        IEnumerable<string> packageRoots,
        IReadOnlyDictionary<string, string> committedPackageHashes,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        if (!TryReadTrustedSdkRestoreGraph(projectPath, properties, out JsonDocument? document) ||
            document is null)
        {
            return;
        }

        using (document)
        {
            if (!TryReadRestoreGraphProject(document.RootElement, projectPath, out JsonElement project) ||
                !project.TryGetProperty("frameworks", out JsonElement frameworks) ||
                frameworks.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var downloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty framework in frameworks.EnumerateObject())
            {
                if (framework.Value.TryGetProperty("dependencies", out JsonElement dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty dependency in dependencies.EnumerateObject())
                        AddTrustedAutoReferencedPackageKeys(
                            dependency,
                            committedPackageHashes,
                            sdkManagedPackageKeys);
                }

                AddSdkDownloadDependencies(framework.Value, downloads);
            }

            foreach (string download in downloads)
                AddSdkDownloadPackageHash(download, packageRoots, hashes);
        }
    }

    private static bool TryReadTrustedSdkRestoreGraph(
        string projectPath,
        JsonElement properties,
        out JsonDocument? document)
    {
        document = null;
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "pf-rg-" + Guid.NewGuid().ToString("N"));
        try
        {
            string intermediateRoot = Directory.CreateDirectory(
                Path.Combine(temporaryRoot, "obj")).FullName + Path.DirectorySeparatorChar;
            string graphPath = Path.Combine(temporaryRoot, "restore-graph.json");
            var arguments = new List<string>
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
                if (properties.TryGetProperty(propertyName, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    arguments.Add(
                        "-p:" + propertyName + "=" + EscapeMsBuildPropertyValue(value.GetString()!));
                }
            }

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(projectPath)!,
                arguments,
                environmentVariables: null,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut || !File.Exists(graphPath))
                return false;

            document = JsonDocument.Parse(File.ReadAllText(graphPath));
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
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch
            {
                // A leftover temporary restore graph cannot make a package trusted.
            }
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

    private static void AddTrustedAutoReferencedPackageKeys(
        JsonProperty dependency,
        IReadOnlyDictionary<string, string> committedPackageHashes,
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

        foreach (KeyValuePair<string, string> package in committedPackageHashes)
        {
            int separator = package.Key.LastIndexOf('|');
            if (separator <= 0 || separator == package.Key.Length - 1 ||
                !package.Key.Substring(0, separator).Equals(
                    dependency.Name,
                    StringComparison.OrdinalIgnoreCase) ||
                !NuGetVersion.TryParse(package.Key.Substring(separator + 1), out NuGetVersion? resolved) ||
                !range.Satisfies(resolved))
            {
                continue;
            }
            sdkManagedPackageKeys.Add(package.Key);
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
