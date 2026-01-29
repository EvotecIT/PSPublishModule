using System;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Computes the next module version using the legacy PSPublishModule "X" stepping logic.
/// </summary>
public sealed class ModuleVersionStepper
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;

    /// <summary>
    /// Creates a new instance using the provided logger and an out-of-process PowerShell runner.
    /// </summary>
    public ModuleVersionStepper(ILogger logger)
        : this(logger, new PowerShellRunner())
    {
    }

    /// <summary>
    /// Creates a new instance using the provided logger and runner.
    /// </summary>
    public ModuleVersionStepper(ILogger logger, IPowerShellRunner runner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _psResourceGet = new PSResourceGetClient(runner, _logger);
    }

    /// <summary>
    /// Steps the provided <paramref name="expectedVersion"/> pattern against the current module version.
    /// When <paramref name="expectedVersion"/> is an exact version, no stepping is performed.
    /// </summary>
    /// <param name="expectedVersion">Exact version or an X-pattern version like <c>0.1.X</c> or <c>0.1.5.X</c>.</param>
    /// <param name="moduleName">Optional module name for resolving current version.</param>
    /// <param name="localPsd1Path">Optional local PSD1 path to resolve current version from.</param>
    /// <param name="repository">Repository name used with PSResourceGet (default: PSGallery).</param>
    /// <param name="prerelease">Whether to include prerelease versions when resolving current version.</param>
    public ModuleVersionStepResult Step(
        string expectedVersion,
        string? moduleName = null,
        string? localPsd1Path = null,
        string repository = "PSGallery",
        bool prerelease = false)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new ArgumentException("ExpectedVersion is required.", nameof(expectedVersion));

        if (Version.TryParse(expectedVersion, out _))
        {
            // Keep legacy behavior: when an exact version is provided, do not attempt auto-versioning.
            return new ModuleVersionStepResult(
                expectedVersion: expectedVersion,
                version: expectedVersion,
                currentVersion: "Not aquired, no auto versioning.",
                currentVersionSource: ModuleVersionSource.None,
                usedAutoVersioning: false);
        }

        var (current, source) = ResolveCurrentVersion(moduleName, localPsd1Path, repository, prerelease);
        var proposed = ComputeNextVersion(expectedVersion, current);

        return new ModuleVersionStepResult(
            expectedVersion: expectedVersion,
            version: proposed,
            currentVersion: current?.ToString(),
            currentVersionSource: source,
            usedAutoVersioning: true);
    }

    private (Version? Version, ModuleVersionSource Source) ResolveCurrentVersion(
        string? moduleName,
        string? localPsd1Path,
        string repository,
        bool prerelease)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return (null, ModuleVersionSource.None);

        if (!string.IsNullOrWhiteSpace(localPsd1Path))
        {
            try
            {
                var full = Path.GetFullPath(localPsd1Path);
                if (!File.Exists(full))
                {
                    _logger.Warn($"Local PSD1 not found: {full}");
                    return (null, ModuleVersionSource.LocalPsd1);
                }

                if (ManifestEditor.TryGetTopLevelString(full, "ModuleVersion", out var v) &&
                    !string.IsNullOrWhiteSpace(v) &&
                    Version.TryParse(v, out var parsed))
                {
                    return (parsed, ModuleVersionSource.LocalPsd1);
                }

                _logger.Warn($"Couldn't parse ModuleVersion from PSD1: {full}");
                return (null, ModuleVersionSource.LocalPsd1);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Couldn't read local PSD1 version: {ex.Message}");
                return (null, ModuleVersionSource.LocalPsd1);
            }
        }

        try
        {
            var results = _psResourceGet.Find(
                new PSResourceFindOptions(
                    names: new[] { moduleName! },
                    version: null,
                    prerelease: prerelease,
                    repositories: new[] { repository }),
                timeout: TimeSpan.FromMinutes(2));

            var hit = results.FirstOrDefault(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (hit is not null && Version.TryParse(hit.Version, out var ver))
            {
                return (ver, ModuleVersionSource.Repository);
            }

            return (null, ModuleVersionSource.Repository);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Couldn't resolve current version from repository: {ex.Message}");
            return (null, ModuleVersionSource.Repository);
        }
    }

    private static string ComputeNextVersion(string expectedVersion, Version? currentVersion)
        => VersionPatternStepper.Step(expectedVersion, currentVersion);
}
