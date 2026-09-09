using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string ResolvePersistentModuleScriptArchiveRoot(
        PowerForgeModuleReleasePlanSummary? plan,
        string configurationDirectory)
    {
        string projectRoot = string.IsNullOrWhiteSpace(plan?.RepositoryRoot)
            ? Path.GetFullPath(configurationDirectory)
            : Path.GetFullPath(plan!.RepositoryRoot);
        string stateRoot = ResolvePersistentReleaseStateRoot(projectRoot);
        string moduleName = SanitizeReleaseStatePathSegment(plan?.ModuleName, "module");
        string version = string.IsNullOrWhiteSpace(plan?.ModuleVersion)
            ? "unversioned"
            : plan!.ModuleVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(plan?.PreReleaseTag))
            version += "-" + plan!.PreReleaseTag!.Trim().TrimStart('-');

        return Path.Combine(
            stateRoot,
            "unified-release-assets",
            moduleName,
            SanitizeReleaseStatePathSegment(version, "unversioned"),
            "modules");
    }

    private static string ResolvePersistentReleaseStateRoot(string projectRoot)
    {
        var current = new DirectoryInfo(Path.GetFullPath(projectRoot));
        while (current is not null)
        {
            string gitMarker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitMarker))
                return Path.Combine(gitMarker, "powerforge");

            if (File.Exists(gitMarker))
            {
                string? marker = File.ReadLines(gitMarker).FirstOrDefault();
                const string prefix = "gitdir:";
                if (!string.IsNullOrWhiteSpace(marker) &&
                    marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string gitDirectory = marker.Substring(prefix.Length).Trim();
                    if (!Path.IsPathRooted(gitDirectory))
                        gitDirectory = Path.GetFullPath(Path.Combine(current.FullName, gitDirectory));
                    return Path.Combine(gitDirectory, "powerforge");
                }
            }

            current = current.Parent;
        }

        string localStateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localStateRoot))
            localStateRoot = Path.GetTempPath();
        return Path.Combine(
            localStateRoot,
            "PowerForge",
            "unified-release-projects",
            CreateReleaseProjectIdentity(projectRoot));
    }

    private static string CreateReleaseProjectIdentity(string projectRoot)
    {
        string canonical = Path.GetFullPath(projectRoot);
        string? fileSystemRoot = Path.GetPathRoot(canonical);
        if (!string.Equals(canonical, fileSystemRoot, FrameworkCompatibility.PathStringComparison()))
        {
            canonical = canonical.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        if (FrameworkCompatibility.IsWindows())
            canonical = canonical.ToUpperInvariant();

        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string SanitizeReleaseStatePathSegment(string? value, string fallback)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new((value ?? string.Empty)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        sanitized = sanitized.Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
