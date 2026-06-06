using System.IO.Compression;
using System.Net.Http;

namespace PowerForge;

public sealed partial class ModuleDependencyInstaller
{
    private const string PowerShellGalleryPackageTemplate = "https://www.powershellgallery.com/api/v2/package/{0}/{1}";

    private bool CanDirectBootstrapPSResourceGet(
        ModuleDependency dependency,
        string? repository,
        RepositoryCredential? credential)
        => dependency is not null &&
           string.Equals(dependency.Name, PSResourceGetModuleName, StringComparison.OrdinalIgnoreCase) &&
           credential is null &&
           (string.IsNullOrWhiteSpace(repository) ||
            string.Equals(repository!.Trim(), "PSGallery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(repository!.Trim(), "PowerShellGallery", StringComparison.OrdinalIgnoreCase));

    private void InstallPSResourceGetDirect(
        ModuleDependency dependency,
        bool prerelease,
        bool force,
        TimeSpan timeout)
    {
        var version = ResolveDirectPSResourceGetVersion(dependency, prerelease, timeout);
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "psresourceget-bootstrap", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(tempRoot, $"{PSResourceGetModuleName}.{version}.nupkg");
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            Directory.CreateDirectory(tempRoot);
            _directPackageDownloader(BuildPowerShellGalleryPackageUri(version), packagePath, timeout);
            ZipFile.ExtractToDirectory(packagePath, extractPath);

            var sourceDirectory = FindPowerShellModulePayloadDirectory(extractPath, PSResourceGetModuleName);
            var destinationRoot = _currentUserModuleRootResolver();
            var destination = Path.Combine(destinationRoot, PSResourceGetModuleName, version);

            if (force && Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);

            Directory.CreateDirectory(destination);
            CopyModulePayload(sourceDirectory, destination);
            _logger.Info($"Installed {PSResourceGetModuleName} {version} directly from PowerShell Gallery to '{destination}'.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    private string ResolveDirectPSResourceGetVersion(
        ModuleDependency dependency,
        bool prerelease,
        TimeSpan timeout)
    {
        if (!string.IsNullOrWhiteSpace(dependency.RequiredVersion))
            return dependency.RequiredVersion!.Trim();

        var minimumVersion = string.IsNullOrWhiteSpace(dependency.MinimumVersion) ? null : dependency.MinimumVersion!.Trim();
        var maximumVersion = string.IsNullOrWhiteSpace(dependency.MaximumVersion) ? null : dependency.MaximumVersion!.Trim();
        var versions = _galleryVersionResolver(PSResourceGetModuleName, prerelease, timeout)
            .Where(version => version is not null && version.IsListed)
            .Where(version => prerelease || !version.IsPrerelease)
            .Where(version => VersionMeetsRange(version.VersionText, minimumVersion, maximumVersion))
            .OrderByDescending(version => version.VersionText, Comparer<string>.Create(CompareVersionStrings))
            .ToArray();

        var selected = versions.FirstOrDefault();
        if (selected is null)
        {
            var rangeText = minimumVersion is null && maximumVersion is null
                ? "latest listed stable version"
                : $"version range '{BuildNuGetRange(minimumVersion, maximumVersion)}'";
            throw new InvalidOperationException($"Unable to resolve {PSResourceGetModuleName} {rangeText} from PowerShell Gallery.");
        }

        return selected.VersionText;
    }

    private static bool VersionMeetsRange(string version, string? minimumVersion, string? maximumVersion)
    {
        if (!string.IsNullOrWhiteSpace(minimumVersion) &&
            CompareVersionStrings(version, minimumVersion) < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(maximumVersion) &&
            CompareVersionStrings(version, maximumVersion) > 0)
        {
            return false;
        }

        return true;
    }

    private static Uri BuildPowerShellGalleryPackageUri(string version)
        => new(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PowerShellGalleryPackageTemplate,
            Uri.EscapeDataString(PSResourceGetModuleName),
            Uri.EscapeDataString(version)));

    private static string FindPowerShellModulePayloadDirectory(string extractPath, string moduleName)
    {
        var manifest = Directory.EnumerateFiles(extractPath, $"{moduleName}.psd1", SearchOption.AllDirectories)
            .Where(path => !IsPackageMetadataPath(extractPath, path))
            .OrderBy(static path => path.Length)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(manifest))
            throw new InvalidOperationException($"Downloaded package did not contain '{moduleName}.psd1'.");

        return Path.GetDirectoryName(manifest!)!;
    }

    private static void CopyModulePayload(string sourceDirectory, string destinationDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsPackageMetadataPath(sourceDirectory, directory))
                continue;

            var relative = GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsPackageMetadataPath(sourceDirectory, file))
                continue;

            var relative = GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsPackageMetadataPath(string root, string path)
    {
        var relative = GetRelativePath(root, path);
        if (relative.Length == 0)
            return false;

        var firstSegment = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstSegment is null)
            return false;

        return string.Equals(firstSegment, "_rels", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstSegment, "package", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstSegment, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveCurrentUserModuleRoot()
    {
        var psModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        var fromPath = (psModulePath ?? string.Empty)
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => path.Trim())
            .FirstOrDefault(path =>
                path.Length > 0 &&
                path.IndexOf("PowerShell", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("Modules", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("Program Files", StringComparison.OrdinalIgnoreCase) < 0);
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath!;

        if (Path.DirectorySeparatorChar == '\\')
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(docs))
                return Path.Combine(docs!, "PowerShell", "Modules");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home!, ".local", "share", "powershell", "Modules");

        return Path.Combine(Path.GetTempPath(), "PowerForge", "Modules");
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
#if NET472
        var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(relativeTo)));
        var pathUri = new Uri(Path.GetFullPath(path));
        var relativeUri = baseUri.MakeRelativeUri(pathUri);
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

#if NET472
    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
#endif

    private static void DownloadPackage(Uri packageUri, string destinationPath, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge/1.0");
        using var response = http.GetAsync(packageUri).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"PowerShell Gallery package download failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{packageUri}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var destination = File.Create(destinationPath);
        source.CopyTo(destination);
    }
}
