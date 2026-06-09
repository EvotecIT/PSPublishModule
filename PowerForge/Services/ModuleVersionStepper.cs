using System;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace PowerForge;

/// <summary>
/// Computes the next module version using the legacy PSPublishModule "X" stepping logic.
/// </summary>
public sealed class ModuleVersionStepper
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly PowerShellGalleryVersionFeedClient _powerShellGalleryFeed;
    private static readonly ModuleManifestMetadataReader ManifestMetadataReader = new();

    /// <summary>
    /// Creates a new instance using the provided logger and an out-of-process PowerShell runner.
    /// </summary>
    public ModuleVersionStepper(ILogger logger)
        : this(logger, new PowerShellRunner(), client: null)
    {
    }

    /// <summary>
    /// Creates a new instance using the provided logger and runner.
    /// </summary>
    public ModuleVersionStepper(ILogger logger, IPowerShellRunner runner)
        : this(logger, runner, client: null)
    {
    }

    internal ModuleVersionStepper(ILogger logger, IPowerShellRunner runner, HttpClient? client)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _psResourceGet = new PSResourceGetClient(runner, _logger);
        _powerShellGalleryFeed = new PowerShellGalleryVersionFeedClient(_logger, client);
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

        var (current, source) = ResolveCurrentVersion(expectedVersion, moduleName, localPsd1Path, repository, prerelease);
        var proposed = ComputeNextVersion(expectedVersion, current);
        proposed = EnsureResolvedVersionIsAvailable(expectedVersion, moduleName, repository, prerelease, proposed);

        return new ModuleVersionStepResult(
            expectedVersion: expectedVersion,
            version: proposed,
            currentVersion: current?.ToString(),
            currentVersionSource: source,
            usedAutoVersioning: true);
    }

    private (Version? Version, ModuleVersionSource Source) ResolveCurrentVersion(
        string expectedVersion,
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

                var metadata = ManifestMetadataReader.Read(full);
                if (!string.IsNullOrWhiteSpace(metadata.ModuleVersion) &&
                    Version.TryParse(metadata.ModuleVersion, out var parsed))
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

        if (string.Equals(repository, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var galleryVersion = TryResolveCurrentVersionFromPowerShellGalleryFeed(moduleName!, prerelease);
                var reservedVersion = TryResolveReservedPowerShellGalleryVersion(expectedVersion, moduleName!, galleryVersion, prerelease);
                if (reservedVersion is not null && (galleryVersion is null || reservedVersion.CompareTo(galleryVersion) > 0))
                {
                    _logger.Verbose($"PowerShell Gallery reserved version for '{moduleName}' was resolved from the exact package metadata endpoint ({reservedVersion}).");
                    return (reservedVersion, ModuleVersionSource.Repository);
                }

                if (galleryVersion is not null)
                    return (galleryVersion, ModuleVersionSource.Repository);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Couldn't resolve current version from the raw PowerShell Gallery feed: {ex.Message}");
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

    private Version? TryResolveReservedPowerShellGalleryVersion(
        string expectedVersion,
        string moduleName,
        Version? currentVersion,
        bool prerelease)
    {
        if (prerelease)
            return null;

        Version? latestReserved = null;
        Version? cursor = currentVersion;
        var missCount = 0;
        var hitCount = 0;
        const int maxProbeCount = 12;
        const int maxConsecutiveMissesAfterHit = 3;
        const int maxConsecutiveMissesBeforeHit = 4;

        for (var index = 0; index < maxProbeCount; index++)
        {
            var candidateText = ComputeNextVersion(expectedVersion, cursor);
            if (!TryParseRepositoryVersion(candidateText, out var candidateVersion))
                break;

            if (_powerShellGalleryFeed.VersionExists(moduleName, candidateText, timeout: TimeSpan.FromSeconds(20)))
            {
                latestReserved = candidateVersion;
                cursor = candidateVersion;
                hitCount++;
                missCount = 0;
                continue;
            }

            cursor = candidateVersion;
            missCount++;
            if (hitCount > 0 && missCount >= maxConsecutiveMissesAfterHit)
                break;
            if (hitCount == 0 && missCount >= maxConsecutiveMissesBeforeHit)
                break;
        }

        return latestReserved;
    }

    private string EnsureResolvedVersionIsAvailable(
        string expectedVersion,
        string? moduleName,
        string repository,
        bool prerelease,
        string proposedVersion)
    {
        if (prerelease ||
            string.IsNullOrWhiteSpace(moduleName) ||
            string.IsNullOrWhiteSpace(proposedVersion) ||
            !string.Equals(repository, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            return proposedVersion;
        }

        var candidateText = proposedVersion;
        const int maxProbeCount = 24;

        for (var index = 0; index < maxProbeCount; index++)
        {
            if (!_powerShellGalleryFeed.VersionExists(moduleName!, candidateText, timeout: TimeSpan.FromSeconds(20)))
                return candidateText;

            if (!TryParseRepositoryVersion(candidateText, out var candidateVersion))
                return candidateText;

            candidateText = ComputeNextVersion(expectedVersion, candidateVersion);
        }

        throw new InvalidOperationException(
            $"Unable to resolve a free PowerShell Gallery version for '{moduleName}' from expected version '{expectedVersion}' after {maxProbeCount} probes.");
    }

    private Version? TryResolveCurrentVersionFromPowerShellGalleryFeed(string moduleName, bool prerelease)
    {
        var versions = _powerShellGalleryFeed.GetVersions(moduleName, prerelease, timeout: TimeSpan.FromMinutes(2));
        Version? latest = null;
        var usedUnlisted = false;

        foreach (var item in versions)
        {
            if (!TryParseRepositoryVersion(item.VersionText, out var parsed))
                continue;

            if (latest is null || parsed.CompareTo(latest) > 0)
            {
                latest = parsed;
                usedUnlisted = !item.IsListed;
            }
        }

        if (latest is not null && usedUnlisted && _logger.IsVerbose)
            _logger.Verbose($"PowerShell Gallery latest version for '{moduleName}' was resolved from an unlisted package entry ({latest}).");

        return latest;
    }

    private static bool TryParseRepositoryVersion(string versionText, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        var dashIndex = versionText.IndexOf('-');
        var numericVersion = dashIndex >= 0 ? versionText.Substring(0, dashIndex) : versionText;
        if (!Version.TryParse(numericVersion, out var parsed))
            return false;

        version = parsed;
        return true;
    }

    private static string ComputeNextVersion(string expectedVersion, Version? currentVersion)
        => VersionPatternStepper.Step(expectedVersion, currentVersion);
}
