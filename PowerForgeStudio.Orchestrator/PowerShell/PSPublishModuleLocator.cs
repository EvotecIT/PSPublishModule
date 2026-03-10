using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.PowerShell;

namespace PowerForgeStudio.Orchestrator.PowerShell;

public static class PSPublishModuleLocator
{
    public static string ResolveModulePath()
    {
        return Resolve().ManifestPath;
    }

    public static PSPublishModuleResolution Resolve()
    {
        var configuredPath = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH");
        var repositoryManifest = ResolveRepositoryManifestPath();
        return Resolve(configuredPath, repositoryManifest ?? string.Empty, GetCandidateModuleRoots());
    }

    internal static PSPublishModuleResolution Resolve(
        string? configuredPath,
        string defaultRepoPath,
        IEnumerable<string>? candidateModuleRoots)
    {
        if (IsUsable(configuredPath))
        {
            return CreateResolution(PSPublishModuleResolutionSource.EnvironmentOverride, configuredPath!, isUsable: true);
        }

        if (IsUsable(defaultRepoPath))
        {
            return CreateResolution(PSPublishModuleResolutionSource.RepositoryManifest, defaultRepoPath, isUsable: true);
        }

        var installedPath = FindInstalledModulePath(candidateModuleRoots ?? GetCandidateModuleRoots());
        if (!string.IsNullOrWhiteSpace(installedPath))
        {
            return CreateResolution(
                PSPublishModuleResolutionSource.InstalledModule,
                installedPath!,
                isUsable: true,
                warning: "Installed PSPublishModule can lag behind repo DSL changes. Point RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH at the intended engine when mixed repos need newer contracts.");
        }

        return CreateResolution(
            PSPublishModuleResolutionSource.FallbackPath,
            defaultRepoPath,
            isUsable: false,
            warning: "No usable PSPublishModule engine was found. Build and publish stages may fail until the module is installed or RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH is set.");
    }

    private static string? FindInstalledModulePath(IEnumerable<string> candidateModuleRoots)
    {
        return candidateModuleRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory => TryParseVersion(directory.Name))
                .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase))
            .Select(directory => Path.Combine(directory.FullName, "PSPublishModule.psd1"))
            .FirstOrDefault(IsUsable);
    }

    private static IEnumerable<string> GetCandidateModuleRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            roots.Add(Path.Combine(documents, "WindowsPowerShell", "Modules", "PSPublishModule"));
            roots.Add(Path.Combine(documents, "PowerShell", "Modules", "PSPublishModule"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "WindowsPowerShell", "Modules", "PSPublishModule"));
            roots.Add(Path.Combine(programFiles, "PowerShell", "Modules", "PSPublishModule"));
        }

        return roots;
    }

    private static string? ResolveRepositoryManifestPath()
    {
        foreach (var candidateRoot in GetRepositoryRootCandidates())
        {
            var manifestPath = Path.Combine(candidateRoot, "Module", "PSPublishModule.psd1");
            if (File.Exists(manifestPath))
            {
                return manifestPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetRepositoryRootCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRootAndParents(candidates, Environment.GetEnvironmentVariable("POWERFORGE_ROOT"));
        AddRootAndParents(candidates, Environment.CurrentDirectory);
        AddRootAndParents(candidates, AppContext.BaseDirectory);
        return candidates;
    }

    private static void AddRootAndParents(ISet<string> candidates, string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return;
        }

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startPath);
            if (!current.Exists && current.Parent is not null)
            {
                current = current.Parent;
            }
        }
        catch
        {
            return;
        }

        while (current is not null)
        {
            candidates.Add(current.FullName);
            var sibling = Path.Combine(current.FullName, "PSPublishModule");
            if (Directory.Exists(sibling))
            {
                candidates.Add(sibling);
            }

            current = current.Parent;
        }
    }

    private static bool IsUsable(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        var moduleRoot = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(moduleRoot))
        {
            return false;
        }

        return File.Exists(Path.Combine(moduleRoot, "PSPublishModule.psm1"))
               && File.Exists(Path.Combine(moduleRoot, "Lib", "Default", "PSPublishModule.dll"))
               && File.Exists(Path.Combine(moduleRoot, "Lib", "Core", "PSPublishModule.dll"));
    }

    private static PSPublishModuleResolution CreateResolution(
        PSPublishModuleResolutionSource source,
        string manifestPath,
        bool isUsable,
        string? warning = null)
    {
        return new PSPublishModuleResolution(
            Source: source,
            ManifestPath: manifestPath,
            ModuleVersion: TryReadModuleVersion(manifestPath),
            IsUsable: isUsable,
            Warning: warning);
    }

    private static string? TryReadModuleVersion(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(manifestPath);
            var version = MatchManifestValue(content, "ModuleVersion");
            var prerelease = MatchManifestValue(content, "Prerelease");
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(prerelease)
                ? version
                : $"{version}-{prerelease}";
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

    private static string? MatchManifestValue(string content, string key)
    {
        var match = Regex.Match(
            content,
            $@"(?im)^\s*{Regex.Escape(key)}\s*=\s*['""](?<value>[^'""]+)['""]");

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static Version TryParseVersion(string value)
        => Version.TryParse(value, out var version) ? version : new Version(0, 0);
}
