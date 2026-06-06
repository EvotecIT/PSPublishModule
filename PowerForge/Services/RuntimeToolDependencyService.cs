using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Shared helper for opt-in runtime installation of tool-style PowerShell module dependencies.
/// </summary>
public sealed class RuntimeToolDependencyService
{
    private const string PSResourceGetModuleName = "Microsoft.PowerShell.PSResourceGet";
    private const string PowerShellGetModuleName = "PowerShellGet";
    private const string PowerShellGetMinimumVersion = "2.2.5";

    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new runtime dependency helper.
    /// </summary>
    public RuntimeToolDependencyService(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures that the requested tool modules are available by installing them when missing.
    /// </summary>
    public IReadOnlyList<ModuleDependencyInstallResult> EnsureInstalled(
        IEnumerable<ModuleDependency> dependencies,
        RuntimeToolDependencyOptions? options = null)
    {
        var list = (dependencies ?? Array.Empty<ModuleDependency>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Name))
            .ToArray();
        if (list.Length == 0)
            return Array.Empty<ModuleDependencyInstallResult>();

        var effective = options ?? new RuntimeToolDependencyOptions();
        var installer = new ModuleDependencyInstaller(_runner, _logger);
        var results = new List<ModuleDependencyInstallResult>();
        var needsRepositoryBootstrap =
            !IsRepositoryToolAvailable() &&
            !list.Any(IsRepositoryToolDependency) &&
            list.Any(dependency => installer.NeedsInstall(dependency, effective.Force));

        if (needsRepositoryBootstrap)
        {
            results.AddRange(installer.EnsureInstalled(
                dependencies: new[] { new ModuleDependency(PSResourceGetModuleName) },
                force: effective.Force,
                prerelease: effective.Prerelease,
                timeoutPerModule: effective.TimeoutPerModule));
        }

        results.AddRange(installer.EnsureInstalled(
            dependencies: list,
            force: effective.Force,
            repository: effective.Repository,
            credential: effective.Credential,
            prerelease: effective.Prerelease,
            preferPowerShellGet: effective.PreferPowerShellGet,
            timeoutPerModule: effective.TimeoutPerModule));
        return results;
    }

    private bool IsRepositoryToolAvailable()
    {
        var psResourceGet = new PSResourceGetClient(_runner, _logger).GetAvailability();
        if (psResourceGet.Available)
            return true;

        var powerShellGet = new PowerShellGetClient(_runner, _logger).GetAvailability();
        return powerShellGet.Available &&
               VersionMeetsMinimum(powerShellGet.Version, PowerShellGetMinimumVersion);
    }

    private static bool IsRepositoryToolDependency(ModuleDependency dependency)
        => dependency is not null &&
           (string.Equals(dependency.Name, PSResourceGetModuleName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dependency.Name, PowerShellGetModuleName, StringComparison.OrdinalIgnoreCase));

    private static bool VersionMeetsMinimum(string? version, string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;
        if (!Version.TryParse(version!.Trim(), out var actual))
            return false;
        return Version.TryParse(minimumVersion, out var minimum) && actual >= minimum;
    }
}
