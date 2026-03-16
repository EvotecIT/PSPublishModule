using System.Text;
using System.Security.Cryptography;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Host;

public static class PowerForgeStudioHostPaths
{
    public static string GetDefaultDatabasePath()
        => Path.Combine(GetStudioRootPath(), "releaseops.db");

    public static string GetWorkspaceDatabasePath(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var workspaceName = GetWorkspaceDisplayName(normalizedRoot);
        var workspaceKey = BuildWorkspaceKey(normalizedRoot);
        var directory = Path.Combine(
            GetStudioRootPath(),
            "workspaces",
            $"{SanitizePathSegment(workspaceName)}-{workspaceKey[..12]}");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "releaseops.db");
    }

    public static string GetWorkspaceRootCatalogPath()
    {
        var directory = Path.Combine(GetStudioRootPath(), "state");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "workspace-roots.json");
    }

    public static string GetPlansFilePath(string repositoryName, string adapterKind, string fileName)
        => GetScopedFilePath(repositoryName, "plans", adapterKind, fileName);

    public static string GetRuntimeFilePath(string repositoryName, string scopeName, string fileName)
        => GetScopedFilePath(repositoryName, "runtime", scopeName, fileName);

    public static string ResolvePSPublishModulePath()
        => PSPublishModuleLocator.ResolveModulePath();

    internal static string GetStudioRootPath(string? localApplicationDataPath = null)
    {
        var root = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;

        return Path.Combine(root, "PowerForgeStudio");
    }

    internal static string GetScopedFilePath(
        string repositoryName,
        string areaName,
        string scopeName,
        string fileName,
        string? localApplicationDataPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var directory = Path.Combine(
            GetStudioRootPath(localApplicationDataPath),
            SanitizePathSegment(areaName),
            SanitizePathSegment(repositoryName),
            SanitizePathSegment(scopeName));

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    public static string NormalizeWorkspaceRoot(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        try
        {
            return Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception) when (value is not null)
        {
            return value.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    internal static string BuildWorkspaceKey(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var bytes = Encoding.UTF8.GetBytes(normalizedRoot);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string GetWorkspaceDisplayName(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var name = Path.GetFileName(normalizedRoot);
        return string.IsNullOrWhiteSpace(name)
            ? "workspace"
            : name;
    }

    internal static string SanitizePathSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
