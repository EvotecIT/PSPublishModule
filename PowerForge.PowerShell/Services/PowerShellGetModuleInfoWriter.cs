using System.Collections;
using System.Management.Automation;

namespace PowerForge;

/// <summary>
/// Writes the PowerShellGet-compatible <c>PSGetModuleInfo.xml</c> metadata requested by save workflows.
/// </summary>
public sealed class PowerShellGetModuleInfoWriter
{
    private const string MetadataFileName = "PSGetModuleInfo.xml";

    /// <summary>
    /// Writes compatibility metadata for a saved module and each saved dependency.
    /// </summary>
    /// <param name="result">Completed managed save result.</param>
    /// <returns>Full paths to metadata files written by the operation.</returns>
    public IReadOnlyList<string> Write(ManagedModuleInstallResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var written = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        WriteResult(result, written, seen);
        return written;
    }

    private static void WriteResult(
        ManagedModuleInstallResult result,
        ICollection<string> written,
        ISet<string> seen)
    {
        if (!result.SavedAsNupkg && !string.IsNullOrWhiteSpace(result.ModulePath))
        {
            var modulePath = Path.GetFullPath(result.ModulePath);
            if (seen.Add(modulePath))
            {
                if (!Directory.Exists(modulePath))
                    throw new DirectoryNotFoundException($"Saved module path was not found: {modulePath}");

                var metadataPath = Path.Combine(modulePath, MetadataFileName);
                File.WriteAllText(metadataPath, PSSerializer.Serialize(CreateMetadata(result), depth: 5));
                if (Path.DirectorySeparatorChar == '\\')
                    File.SetAttributes(metadataPath, File.GetAttributes(metadataPath) | FileAttributes.Hidden);
                written.Add(metadataPath);
            }
        }

        foreach (var dependency in result.DependencyResults ?? Array.Empty<ManagedModuleInstallResult>())
            WriteResult(dependency, written, seen);
    }

    private static PSObject CreateMetadata(ManagedModuleInstallResult result)
    {
        var package = result.Download?.Metadata;
        var manifestPath = ResolveManifestPath(result, package);
        var exports = manifestPath is null ? null : ModuleManifestExportReader.ReadExports(manifestPath);
        var manifestTags = manifestPath is null
            ? Array.Empty<string>()
            : ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Tags");
        var installedDate = result.Receipt?.CompletedAtUtc.LocalDateTime ?? DateTime.Now;
        var info = new PSObject();
        Add(info, "Name", result.Name);
        Add(info, "Version", result.Version);
        Add(info, "Type", 1);
        Add(info, "Description", ReadManifestString(manifestPath, "Description") ?? package?.Description);
        Add(info, "Author", ReadManifestString(manifestPath, "Author") ?? package?.Authors);
        Add(info, "CompanyName", ReadManifestString(manifestPath, "CompanyName"));
        Add(info, "Copyright", ReadManifestString(manifestPath, "Copyright"));
        Add(info, "PublishedDate", installedDate);
        Add(info, "InstalledDate", installedDate);
        Add(info, "IsPrerelease", ManagedModuleVersionComparer.IsPrerelease(result.Version));
        Add(info, "UpdatedDate", null);
        Add(info, "LicenseUri", ToUri(ReadManifestPsDataString(manifestPath, "LicenseUri") ?? package?.License));
        Add(info, "ProjectUri", ToUri(ReadManifestPsDataString(manifestPath, "ProjectUri") ?? package?.ProjectUrl));
        Add(info, "IconUri", ToUri(ReadManifestPsDataString(manifestPath, "IconUri")));
        Add(info, "Tags", package?.Tags?.Count > 0 ? package.Tags.ToArray() : manifestTags);
        Add(info, "Includes", CreateIncludes(manifestPath, exports));
        Add(info, "PowerShellGetFormatVersion", string.Empty);
        Add(info, "ReleaseNotes", ReadManifestPsDataString(manifestPath, "ReleaseNotes"));
        Add(info, "Dependencies", CreateDependencies(package));
        Add(info, "RepositorySourceLocation", result.RepositorySource);
        Add(info, "Repository", result.RepositoryName);
        Add(info, "AdditionalMetadata", CreateAdditionalMetadata(result.Version));
        Add(info, "InstalledLocation", result.ModulePath);
        return info;
    }

    private static Hashtable CreateIncludes(string? manifestPath, ExportSet? exports)
    {
        var functions = exports?.Functions ?? Array.Empty<string>();
        var cmdlets = exports?.Cmdlets ?? Array.Empty<string>();
        var aliases = exports?.Aliases ?? Array.Empty<string>();
        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["DscResource"] = manifestPath is null
                ? Array.Empty<string>()
                : ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "DscResourcesToExport"),
            ["RoleCapability"] = null,
            ["Workflow"] = null,
            ["Function"] = functions.Length == 0 ? null : functions,
            ["Command"] = functions.Concat(cmdlets).Concat(aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["Cmdlet"] = cmdlets
        };
    }

    private static string? ResolveManifestPath(
        ManagedModuleInstallResult result,
        ManagedModulePackageMetadata? package)
    {
        var modulePath = result.ModulePath;
        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            return null;

        var declaredManifestPath = package?.ModuleManifestPath;
        if (!string.IsNullOrWhiteSpace(declaredManifestPath))
        {
            var relativePath = declaredManifestPath!.Replace('/', Path.DirectorySeparatorChar);
            var packageManifestPath = Path.Combine(modulePath!, relativePath);
            if (File.Exists(packageManifestPath))
                return packageManifestPath;

            var rootManifestPath = Path.Combine(modulePath!, Path.GetFileName(relativePath));
            if (File.Exists(rootManifestPath))
                return rootManifestPath;
        }

        var namedManifestPath = Path.Combine(modulePath!, result.Name + ".psd1");
        if (File.Exists(namedManifestPath))
            return namedManifestPath;

        return Directory.EnumerateFiles(modulePath!, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private static string? ReadManifestString(string? manifestPath, string key)
        => manifestPath is null ? null : ModuleManifestValueReader.ReadTopLevelString(manifestPath, key);

    private static string? ReadManifestPsDataString(string? manifestPath, string key)
        => manifestPath is null
            ? null
            : ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, key).FirstOrDefault();

    private static object[] CreateDependencies(ManagedModulePackageMetadata? package)
        => package?.Dependencies?
            .Select(static dependency =>
            {
                var item = new PSObject();
                Add(item, "Name", dependency.Id);
                Add(item, "VersionRange", dependency.VersionRange);
                return (object)item;
            })
            .ToArray() ?? Array.Empty<object>();

    private static PSObject CreateAdditionalMetadata(string version)
    {
        var metadata = new PSObject();
        Add(metadata, "NormalizedVersion", version);
        Add(metadata, "IsPrerelease", ManagedModuleVersionComparer.IsPrerelease(version).ToString());
        return metadata;
    }

    private static Uri? ToUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static void Add(PSObject target, string name, object? value)
        => target.Properties.Add(new PSNoteProperty(name, value));

    private static StringComparer PathComparer
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
