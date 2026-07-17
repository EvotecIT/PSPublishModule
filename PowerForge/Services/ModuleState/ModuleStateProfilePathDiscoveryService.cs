using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateProfilePathDiscoveryService
{
    private readonly Func<string, ModuleStateDirectoryProbeResult> _directoryProbe;

    internal ModuleStateProfilePathDiscoveryService()
        : this(ModuleStateDirectoryProbe.Probe)
    {
    }

    internal ModuleStateProfilePathDiscoveryService(Func<string, ModuleStateDirectoryProbeResult> directoryProbe)
    {
        _directoryProbe = directoryProbe ?? throw new ArgumentNullException(nameof(directoryProbe));
    }

    internal ModuleStateProfilePathDiscoveryResult Discover(
        IEnumerable<string>? profilePaths = null,
        bool includeAllLocalProfiles = false,
        string? localProfilesRoot = null)
        => Discover(
            profilePaths,
            includeAllLocalProfiles,
            localProfilesRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal ModuleStateProfilePathDiscoveryResult Discover(
        IEnumerable<string>? profilePaths,
        bool includeAllLocalProfiles,
        string? localProfilesRoot,
        string? currentUserProfilePath)
    {
        var diagnostics = new List<ModuleStateInventoryDiagnostic>();
        var requestedProfiles = new List<ProfileCandidate>();
        foreach (var profilePath in profilePaths ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(profilePath))
                requestedProfiles.Add(new ProfileCandidate(Path.GetFullPath(profilePath.Trim()), isRequired: true));
        }

        if (includeAllLocalProfiles)
        {
            foreach (var profilePath in DiscoverLocalProfileDirectories(localProfilesRoot, currentUserProfilePath, diagnostics))
                requestedProfiles.Add(new ProfileCandidate(profilePath, isRequired: false));
        }

        var paths = new List<ModuleStateModulePath>();
        var seenProfiles = new HashSet<string>(ModuleStatePathIdentity.Comparer);
        var seenRoots = new HashSet<string>(ModuleStatePathIdentity.Comparer);
        foreach (var candidate in requestedProfiles)
        {
            if (!seenProfiles.Add(candidate.Path))
                continue;

            var profileProbe = _directoryProbe(candidate.Path);
            if (profileProbe.Status != ModuleStateDirectoryProbeStatus.Available)
            {
                if (candidate.IsRequired)
                {
                    diagnostics.Add(new ModuleStateInventoryDiagnostic(
                        ModuleStateConflictSeverity.Error,
                        profileProbe.Status == ModuleStateDirectoryProbeStatus.Missing
                            ? "ModuleState.UserProfileMissing"
                            : "ModuleState.UserProfileUnavailable",
                        profileProbe.Status == ModuleStateDirectoryProbeStatus.Missing
                            ? $"Required user profile path '{candidate.Path}' does not exist."
                            : $"Required user profile path '{candidate.Path}' could not be inspected: {profileProbe.Reason}",
                        candidate.Path,
                        profileName: Path.GetFileName(candidate.Path)));
                }
                else
                {
                    diagnostics.Add(new ModuleStateInventoryDiagnostic(
                        ModuleStateConflictSeverity.Warning,
                        "ModuleState.UserProfileUnavailable",
                        $"Discovered user profile path '{candidate.Path}' is no longer accessible.",
                        candidate.Path,
                        profileName: Path.GetFileName(candidate.Path)));
                }

                continue;
            }

            var profileName = new DirectoryInfo(candidate.Path).Name;
            var discoveredForProfile = 0;
            foreach (var moduleRoot in EnumerateStandardModuleRoots(candidate.Path, profileName))
            {
                var rootProbe = _directoryProbe(moduleRoot.Path);
                var rootExists = rootProbe.Status == ModuleStateDirectoryProbeStatus.Available;
                if (rootProbe.Status == ModuleStateDirectoryProbeStatus.Inaccessible)
                {
                    diagnostics.Add(CreateUnavailableModuleRootDiagnostic(
                        moduleRoot,
                        rootProbe.Reason ?? "The module root could not be inspected.",
                        candidate.IsRequired ? ModuleStateConflictSeverity.Error : ModuleStateConflictSeverity.Warning));
                    if (candidate.IsRequired && seenRoots.Add(moduleRoot.Path))
                        paths.Add(WithRequired(moduleRoot));
                    continue;
                }
                if (!candidate.IsRequired && !rootExists)
                {
                    continue;
                }
                if (!seenRoots.Add(moduleRoot.Path))
                    continue;

                paths.Add(candidate.IsRequired && rootExists
                    ? WithRequired(moduleRoot)
                    : moduleRoot);
                if (rootExists)
                    discoveredForProfile++;
            }

            if (candidate.IsRequired && discoveredForProfile == 0)
            {
                diagnostics.Add(new ModuleStateInventoryDiagnostic(
                    ModuleStateConflictSeverity.Warning,
                    "ModuleState.UserProfileHasNoModuleRoots",
                    $"User profile '{candidate.Path}' has no standard PowerShell module roots.",
                    candidate.Path,
                    scope: "CurrentUser",
                    profileName: profileName));
            }
        }

        return new ModuleStateProfilePathDiscoveryResult(paths.ToArray(), diagnostics.ToArray());
    }

    private static ModuleStateModulePath WithRequired(ModuleStateModulePath moduleRoot)
        => new(
            moduleRoot.Path,
            moduleRoot.PowerShellEdition,
            moduleRoot.Scope,
            moduleRoot.ProfileName,
            isRequired: true);

    private static ModuleStateInventoryDiagnostic CreateUnavailableModuleRootDiagnostic(
        ModuleStateModulePath moduleRoot,
        string reason,
        ModuleStateConflictSeverity severity)
        => new(
            severity,
            "ModuleState.UserProfileModuleRootUnavailable",
            $"User-profile module root '{moduleRoot.Path}' could not be inspected: {reason}",
            moduleRoot.Path,
            moduleRoot.PowerShellEdition,
            moduleRoot.Scope,
            moduleRoot.ProfileName);

    private static IEnumerable<string> DiscoverLocalProfileDirectories(
        string? localProfilesRoot,
        string? currentUserProfilePath,
        ICollection<ModuleStateInventoryDiagnostic> diagnostics)
    {
        var currentProfile = string.IsNullOrWhiteSpace(currentUserProfilePath)
            ? null
            : Path.GetFullPath(currentUserProfilePath!.Trim());
        var parent = ResolveLocalProfileContainer(
            localProfilesRoot,
            currentProfile,
            FrameworkCompatibility.IsWindows());
        var profiles = new List<string>();
        if (string.IsNullOrWhiteSpace(localProfilesRoot) &&
            !string.IsNullOrWhiteSpace(currentProfile) &&
            Directory.Exists(currentProfile))
            profiles.Add(currentProfile!);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            diagnostics.Add(new ModuleStateInventoryDiagnostic(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.LocalProfileDiscoveryUnavailable",
                "The local user-profile container could not be resolved or does not exist. Use UserProfilePath or ModulePath for redirected or custom profiles.",
                parent ?? "<local-profile-container>"));
            return profiles;
        }

        try
        {
            return profiles
                .Concat(Directory.EnumerateDirectories(parent!, "*", SearchOption.TopDirectoryOnly))
                .Distinct(ModuleStatePathIdentity.Comparer)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            diagnostics.Add(new ModuleStateInventoryDiagnostic(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.LocalProfileDiscoveryFailed",
                $"The local user-profile container '{parent}' could not be enumerated: {ex.Message}",
                parent!));
            return profiles;
        }
    }

    internal static string? ResolveLocalProfileContainer(
        string? localProfilesRoot,
        string? currentUserProfilePath,
        bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(localProfilesRoot))
            return Path.GetFullPath(localProfilesRoot!.Trim());
        if (string.IsNullOrWhiteSpace(currentUserProfilePath))
            return null;

        if (isWindows)
            return Directory.GetParent(currentUserProfilePath!)?.FullName;

        var normalized = currentUserProfilePath!.Trim().Replace('\\', '/').TrimEnd('/');
        if (string.Equals(normalized, "/root", StringComparison.Ordinal))
            return "/home";
        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? "/" : normalized.Substring(0, separator);
    }

    private static IEnumerable<ModuleStateModulePath> EnumerateStandardModuleRoots(
        string profilePath,
        string profileName)
    {
        if (FrameworkCompatibility.IsWindows())
        {
            yield return new ModuleStateModulePath(
                Path.Combine(profilePath, "Documents", "PowerShell", "Modules"),
                powerShellEdition: "Core",
                scope: "CurrentUser",
                profileName: profileName);
            yield return new ModuleStateModulePath(
                Path.Combine(profilePath, "Documents", "WindowsPowerShell", "Modules"),
                powerShellEdition: "Desktop",
                scope: "CurrentUser",
                profileName: profileName);
            yield break;
        }

        yield return new ModuleStateModulePath(
            Path.Combine(profilePath, ".local", "share", "powershell", "Modules"),
            powerShellEdition: "Core",
            scope: "CurrentUser",
            profileName: profileName);
    }

    private readonly struct ProfileCandidate
    {
        internal ProfileCandidate(string path, bool isRequired)
        {
            Path = path;
            IsRequired = isRequired;
        }

        internal string Path { get; }

        internal bool IsRequired { get; }
    }
}

internal sealed class ModuleStateProfilePathDiscoveryResult
{
    internal ModuleStateProfilePathDiscoveryResult(
        ModuleStateModulePath[] modulePaths,
        ModuleStateInventoryDiagnostic[] diagnostics)
    {
        ModulePaths = modulePaths ?? Array.Empty<ModuleStateModulePath>();
        Diagnostics = diagnostics ?? Array.Empty<ModuleStateInventoryDiagnostic>();
    }

    internal ModuleStateModulePath[] ModulePaths { get; }

    internal ModuleStateInventoryDiagnostic[] Diagnostics { get; }
}
