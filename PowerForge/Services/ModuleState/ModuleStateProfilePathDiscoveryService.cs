using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateProfilePathDiscoveryService
{
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

            if (!Directory.Exists(candidate.Path))
            {
                if (candidate.IsRequired)
                {
                    diagnostics.Add(new ModuleStateInventoryDiagnostic(
                        ModuleStateConflictSeverity.Error,
                        "ModuleState.UserProfileMissing",
                        $"Required user profile path '{candidate.Path}' does not exist.",
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
                if (!candidate.IsRequired && !Directory.Exists(moduleRoot.Path))
                {
                    TryReportUnavailableModuleRoot(moduleRoot, diagnostics);
                    continue;
                }
                if (!seenRoots.Add(moduleRoot.Path))
                    continue;

                paths.Add(moduleRoot);
                if (Directory.Exists(moduleRoot.Path))
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

    private static void TryReportUnavailableModuleRoot(
        ModuleStateModulePath moduleRoot,
        ICollection<ModuleStateInventoryDiagnostic> diagnostics)
    {
        try
        {
            var attributes = File.GetAttributes(moduleRoot.Path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                diagnostics.Add(CreateUnavailableModuleRootDiagnostic(
                    moduleRoot,
                    "The discovered module root exists but could not be inspected."));
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            diagnostics.Add(CreateUnavailableModuleRootDiagnostic(moduleRoot, ex.Message));
        }
    }

    private static ModuleStateInventoryDiagnostic CreateUnavailableModuleRootDiagnostic(
        ModuleStateModulePath moduleRoot,
        string reason)
        => new(
            ModuleStateConflictSeverity.Warning,
            "ModuleState.UserProfileModuleRootUnavailable",
            $"Discovered user-profile module root '{moduleRoot.Path}' could not be inspected: {reason}",
            moduleRoot.Path,
            moduleRoot.PowerShellEdition,
            moduleRoot.Scope,
            moduleRoot.ProfileName);

    private static IEnumerable<string> DiscoverLocalProfileDirectories(
        string? localProfilesRoot,
        string? currentUserProfilePath,
        ICollection<ModuleStateInventoryDiagnostic> diagnostics)
    {
        var parent = string.IsNullOrWhiteSpace(localProfilesRoot)
            ? string.IsNullOrWhiteSpace(currentUserProfilePath)
                ? null
                : Directory.GetParent(currentUserProfilePath)?.FullName
            : Path.GetFullPath(localProfilesRoot!.Trim());
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            diagnostics.Add(new ModuleStateInventoryDiagnostic(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.LocalProfileDiscoveryUnavailable",
                "The local user-profile container could not be resolved or does not exist. Use UserProfilePath or ModulePath for redirected or custom profiles.",
                parent ?? "<local-profile-container>"));
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(parent!, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            diagnostics.Add(new ModuleStateInventoryDiagnostic(
                ModuleStateConflictSeverity.Warning,
                "ModuleState.LocalProfileDiscoveryFailed",
                $"The local user-profile container '{parent}' could not be enumerated: {ex.Message}",
                parent!));
            return Array.Empty<string>();
        }
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
