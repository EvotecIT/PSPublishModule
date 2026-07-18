using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleStateProfilePathDiscoveryTests
{
    [Fact]
    public void ProfileDiscovery_EnumeratesStandardRootsAcrossLocalProfiles()
    {
        using var workspace = new TemporaryDirectory();
        var aliceRoot = StandardCoreProfileModuleRoot(Path.Combine(workspace.Path, "Alice"));
        var bobRoot = OperatingSystem.IsWindows()
            ? Path.Combine(workspace.Path, "Bob", "Documents", "WindowsPowerShell", "Modules")
            : StandardCoreProfileModuleRoot(Path.Combine(workspace.Path, "Bob"));
        Directory.CreateDirectory(aliceRoot);
        Directory.CreateDirectory(bobRoot);

        var discovery = new ModuleStateProfilePathDiscoveryService().Discover(
            includeAllLocalProfiles: true,
            localProfilesRoot: workspace.Path);

        Assert.Empty(discovery.Diagnostics);
        Assert.Contains(discovery.ModulePaths, path => path.ProfileName == "Alice" && path.PowerShellEdition == "Core" && PathsEqual(path.Path, aliceRoot));
        Assert.Contains(discovery.ModulePaths, path => path.ProfileName == "Bob" &&
            path.PowerShellEdition == (OperatingSystem.IsWindows() ? "Desktop" : "Core") && PathsEqual(path.Path, bobRoot));
    }

    [Fact]
    public void ProfileDiscovery_ReportsUnavailableLocalProfileContainer()
    {
        using var workspace = new TemporaryDirectory();
        var missingProfilesRoot = Path.Combine(workspace.Path, "missing-profiles");

        var discovery = new ModuleStateProfilePathDiscoveryService().Discover(
            includeAllLocalProfiles: true,
            localProfilesRoot: missingProfilesRoot);

        var diagnostic = Assert.Single(discovery.Diagnostics);
        Assert.Equal("ModuleState.LocalProfileDiscoveryUnavailable", diagnostic.Code);
        Assert.Equal(ModuleStateConflictSeverity.Warning, diagnostic.Severity);
        Assert.Empty(discovery.ModulePaths);
    }

    [Fact]
    public void ProfileDiscovery_ReportsStableDiagnosticWhenCurrentProfileCannotBeResolved()
    {
        var discovery = new ModuleStateProfilePathDiscoveryService().Discover(
            profilePaths: null,
            includeAllLocalProfiles: true,
            localProfilesRoot: null,
            currentUserProfilePath: string.Empty);

        var diagnostic = Assert.Single(discovery.Diagnostics);
        Assert.Equal("ModuleState.LocalProfileDiscoveryUnavailable", diagnostic.Code);
        Assert.Equal("<local-profile-container>", diagnostic.Path);
        Assert.Empty(discovery.ModulePaths);
    }

    [Fact]
    public void ProfileDiscovery_UsesHomeContainerForUnixRootAccount()
    {
        var container = ModuleStateProfilePathDiscoveryService.ResolveLocalProfileContainer(
            localProfilesRoot: null,
            currentUserProfilePath: "/root",
            isWindows: false);

        Assert.Equal("/home", container);
    }

    [Fact]
    public void ProfileDiscovery_PreservesLiteralBackslashesInUnixProfilePaths()
    {
        var container = ModuleStateProfilePathDiscoveryService.ResolveLocalProfileContainer(
            localProfilesRoot: null,
            currentUserProfilePath: @"/srv/profiles/alice\ops",
            isWindows: false);

        Assert.Equal("/srv/profiles", container);
    }

    [Fact]
    public void ProfileDiscovery_ResolvesExplicitPlacementByFullPathWhenLeafNamesCollide()
    {
        using var workspace = new TemporaryDirectory();
        var explicitProfile = Path.Combine(workspace.Path, "redirected", "alice");
        var localProfilesRoot = Path.Combine(workspace.Path, "home");
        var discoveredProfile = Path.Combine(localProfilesRoot, "alice");
        Directory.CreateDirectory(explicitProfile);
        Directory.CreateDirectory(StandardCoreProfileModuleRoot(discoveredProfile));
        var discovery = new ModuleStateProfilePathDiscoveryService().Discover(
            profilePaths: new[] { explicitProfile },
            includeAllLocalProfiles: true,
            localProfilesRoot: localProfilesRoot);

        var placement = ModuleStateProfilePathDiscoveryService.ResolveExplicitProfilePlacement(
            explicitProfile,
            "Core",
            discovery.ModulePaths);

        Assert.NotNull(placement);
        Assert.True(PathsEqual(StandardCoreProfileModuleRoot(explicitProfile), placement!.Path));
    }

    [Fact]
    public void ProfileDiscovery_ResolvesExplicitPlacementWhenAnotherProfileIsNestedBelowIt()
    {
        using var workspace = new TemporaryDirectory();
        var explicitProfile = workspace.Path;
        var nestedProfile = Path.Combine(explicitProfile, "alice");
        var explicitRoot = StandardCoreProfileModuleRoot(explicitProfile);
        var nestedRoot = StandardCoreProfileModuleRoot(nestedProfile);
        var discoveredModulePaths = new[]
        {
            new ModuleStateModulePath(explicitRoot, "Core", "CurrentUser"),
            new ModuleStateModulePath(nestedRoot, "Core", "CurrentUser")
        };

        var placement = ModuleStateProfilePathDiscoveryService.ResolveExplicitProfilePlacement(
            explicitProfile,
            "Core",
            discoveredModulePaths);

        Assert.NotNull(placement);
        Assert.True(PathsEqual(explicitRoot, placement!.Path));
    }

    [Fact]
    public void ProfileDiscovery_MarksExistingExplicitProfileRootsRequired()
    {
        using var workspace = new TemporaryDirectory();
        var profilePath = Path.Combine(workspace.Path, "Alice");
        Directory.CreateDirectory(profilePath);
        var existingRoot = StandardCoreProfileModuleRoot(profilePath);
        Directory.CreateDirectory(existingRoot);

        var discovery = new ModuleStateProfilePathDiscoveryService().Discover(
            profilePaths: new[] { profilePath });

        var coreRoot = Assert.Single(discovery.ModulePaths, path =>
            string.Equals(path.PowerShellEdition, "Core", StringComparison.OrdinalIgnoreCase));
        Assert.True(coreRoot.IsRequired);
        Assert.True(PathsEqual(existingRoot, coreRoot.Path));
        if (OperatingSystem.IsWindows())
        {
            var desktopRoot = Assert.Single(discovery.ModulePaths, path =>
                string.Equals(path.PowerShellEdition, "Desktop", StringComparison.OrdinalIgnoreCase));
            Assert.False(desktopRoot.IsRequired);
            Assert.False(Directory.Exists(desktopRoot.Path));
        }
    }

    [Fact]
    public void ProfileDiscovery_MarksInaccessibleExplicitProfileRootRequiredAndBlocking()
    {
        using var workspace = new TemporaryDirectory();
        var profilePath = Path.Combine(workspace.Path, "Alice");
        Directory.CreateDirectory(profilePath);
        var inaccessibleRoot = StandardCoreProfileModuleRoot(profilePath);
        var discoveryService = new ModuleStateProfilePathDiscoveryService(path =>
            PathsEqual(path, inaccessibleRoot)
                ? new ModuleStateDirectoryProbeResult(
                    ModuleStateDirectoryProbeStatus.Inaccessible,
                    "Access denied by test probe.")
                : ModuleStateDirectoryProbe.Probe(path));

        var discovery = discoveryService.Discover(profilePaths: new[] { profilePath });

        var coreRoot = Assert.Single(discovery.ModulePaths, path =>
            string.Equals(path.PowerShellEdition, "Core", StringComparison.OrdinalIgnoreCase));
        Assert.True(coreRoot.IsRequired);
        Assert.True(PathsEqual(inaccessibleRoot, coreRoot.Path));
        var diagnostic = Assert.Single(discovery.Diagnostics, static diagnostic =>
            diagnostic.Code == "ModuleState.UserProfileModuleRootUnavailable");
        Assert.Equal(ModuleStateConflictSeverity.Error, diagnostic.Severity);
        Assert.Contains("Access denied", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string StandardCoreProfileModuleRoot(string profilePath)
        => OperatingSystem.IsWindows()
            ? Path.Combine(profilePath, "Documents", "PowerShell", "Modules")
            : Path.Combine(profilePath, ".local", "share", "powershell", "Modules");

    private static bool PathsEqual(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
               ? StringComparison.OrdinalIgnoreCase
               : StringComparison.Ordinal);
}
