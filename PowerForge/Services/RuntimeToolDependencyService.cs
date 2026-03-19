using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Shared helper for opt-in runtime installation of tool-style PowerShell module dependencies.
/// </summary>
public sealed class RuntimeToolDependencyService
{
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
        return installer.EnsureInstalled(
            dependencies: list,
            force: effective.Force,
            repository: effective.Repository,
            credential: effective.Credential,
            prerelease: effective.Prerelease,
            preferPowerShellGet: effective.PreferPowerShellGet,
            timeoutPerModule: effective.TimeoutPerModule);
    }
}
