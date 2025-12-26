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
    {
        var parts = expectedVersion.Split('.');
        var segs = new string?[4];
        for (int i = 0; i < 4; i++)
            segs[i] = i < parts.Length ? parts[i] : null;

        var stepIndex = -1;
        for (int i = 0; i < segs.Length; i++)
        {
            if (string.Equals(segs[i], "X", StringComparison.OrdinalIgnoreCase))
            {
                stepIndex = i;
                break;
            }
        }

        if (stepIndex < 0)
            throw new ArgumentException("ExpectedVersion must contain an 'X' placeholder (or be an exact version).", nameof(expectedVersion));

        var prepared = new int?[4];
        for (int i = 0; i < segs.Length; i++)
        {
            var s = segs[i];
            if (string.IsNullOrWhiteSpace(s)) { prepared[i] = null; continue; }
            if (i == stepIndex) { prepared[i] = null; continue; }

            if (!int.TryParse(s, out var v))
                throw new ArgumentException($"ExpectedVersion segment '{s}' is not a number.", nameof(expectedVersion));
            prepared[i] = v;
        }

        var baseline = currentVersion ?? new Version(0, 0, 0, 0);
        var stepValue = currentVersion is null ? 1 : GetPart(currentVersion, stepIndex);
        if (stepValue < 0) stepValue = 0;

        prepared[stepIndex] = stepValue;

        var candidate = CreateVersion(prepared);
        if (candidate.CompareTo(baseline) > 0)
        {
            prepared[stepIndex] = 0;
            candidate = CreateVersion(prepared);
        }

        while (candidate.CompareTo(baseline) <= 0)
        {
            prepared[stepIndex] = (prepared[stepIndex] ?? 0) + 1;
            candidate = CreateVersion(prepared);
        }

        return candidate.ToString();
    }

    private static int GetPart(Version v, int index)
        => index switch
        {
            0 => v.Major,
            1 => v.Minor,
            2 => v.Build,
            3 => v.Revision,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

    private static Version CreateVersion(int?[] parts)
    {
        if (parts is null) throw new ArgumentNullException(nameof(parts));

        var lastNonNull = -1;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].HasValue) lastNonNull = i;

        if (!parts[0].HasValue || !parts[1].HasValue)
            throw new InvalidOperationException("ExpectedVersion must include at least major and minor values.");

        var major = parts[0]!.Value;
        var minor = parts[1]!.Value;

        if (lastNonNull <= 1) return new Version(major, minor);

        var build = parts[2] ?? 0;
        if (lastNonNull == 2) return new Version(major, minor, build);

        var revision = parts[3] ?? 0;
        return new Version(major, minor, build, revision);
    }
}
