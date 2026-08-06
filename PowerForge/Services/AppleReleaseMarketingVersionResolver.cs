using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Resolves an Apple marketing-version pattern against local, TestFlight, and
/// App Store release trains.
/// </summary>
internal static class AppleReleaseMarketingVersionResolver
{
    private static readonly Regex Pattern = new(
        @"^\d+\.(?:\d+|X)\.(?:\d+|X)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static void ValidatePattern(string pattern, string settingName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException($"{settingName} is required.");

        var value = pattern.Trim();
        if (!Pattern.IsMatch(value) || value.Count(static character => character is 'X' or 'x') != 1)
        {
            throw new InvalidOperationException(
                $"{settingName} must be a three-part Apple version pattern with exactly one X placeholder, for example 1.X.0 or 1.6.X.");
        }

        try
        {
            _ = VersionPatternStepper.Step(value, currentVersion: null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException($"{settingName} is invalid: {exception.Message}", exception);
        }
    }

    internal static AppleReleaseMarketingVersionResolution Resolve(
        string pattern,
        string localMarketingVersion,
        IEnumerable<AppStoreConnectVersionInfo> appStoreVersions,
        IEnumerable<AppStoreConnectBuildInfo> builds)
    {
        ValidatePattern(pattern, "Apple marketing version pattern");

        var local = ParseKnownVersion(localMarketingVersion, "local Apple version source");
        var storeVersions = (appStoreVersions ?? Array.Empty<AppStoreConnectVersionInfo>()).ToArray();
        var testFlightBuilds = (builds ?? Array.Empty<AppStoreConnectBuildInfo>()).ToArray();
        ValidateRemoteEvidence(storeVersions, testFlightBuilds);

        var storeEvidence = storeVersions
            .Select(value => new StoreVersionEvidence(
                ParseKnownVersion(value.VersionString!, "App Store Connect version"),
                FirstNonEmpty(value.AppStoreState, value.AppVersionState)))
            .ToArray();
        var testFlightVersions = testFlightBuilds
            .Select(value => ParseKnownVersion(value.MarketingVersion!, "TestFlight build marketing version"))
            .ToArray();
        var remoteVersions = storeEvidence.Select(static value => value.Version)
            .Concat(testFlightVersions)
            .ToArray();

        var occupiedVersions = storeEvidence
            .Where(static value => !IsReusableAppStoreVersion(value.State))
            .Select(static value => value.Version)
            .Distinct()
            .ToArray();
        var highestOccupied = Highest(occupiedVersions);

        var reusableCandidates = new[] { local }
            .Concat(testFlightVersions)
            .Concat(storeEvidence
                .Where(static value => IsReusableAppStoreVersion(value.State))
                .Select(static value => value.Version))
            .Where(version => VersionPatternStepper.CanRepresent(pattern, version.ToString(3)))
            .Where(version => !occupiedVersions.Contains(version))
            .Where(version => highestOccupied is null || version.CompareTo(highestOccupied) > 0)
            .Distinct()
            .OrderByDescending(static version => version)
            .ToArray();

        var reusable = reusableCandidates.FirstOrDefault();
        var highestKnown = Highest(new[] { local }.Concat(remoteVersions));
        var resolved = reusable ?? ParseResolvedPatternVersion(
            VersionPatternStepper.Step(pattern.Trim(), highestKnown),
            pattern);

        return new AppleReleaseMarketingVersionResolution
        {
            Pattern = pattern.Trim(),
            MarketingVersion = resolved.ToString(3),
            HighestRemoteMarketingVersion = Highest(remoteVersions)?.ToString(3),
            ReusedUnreleasedMarketingVersion = reusable is not null
        };
    }

    private static bool IsReusableAppStoreVersion(string? state)
        => state is not null && ReusableAppStoreStates.Contains(state);

    private static void ValidateRemoteEvidence(
        IEnumerable<AppStoreConnectVersionInfo> storeVersions,
        IEnumerable<AppStoreConnectBuildInfo> builds)
    {
        if (storeVersions.Any(static value => string.IsNullOrWhiteSpace(value.VersionString)))
        {
            throw new InvalidOperationException(
                "App Store Connect returned a version without a marketing-version identity. Resolve the incomplete remote version before automatic selection.");
        }

        if (builds.Any(static value => string.IsNullOrWhiteSpace(value.MarketingVersion)))
        {
            throw new InvalidOperationException(
                "App Store Connect returned a TestFlight build without a marketing-version identity. Resolve the incomplete remote build before automatic selection.");
        }
    }

    private static readonly HashSet<string> ReusableAppStoreStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "PREPARE_FOR_SUBMISSION",
        "READY_FOR_REVIEW",
        "DEVELOPER_REJECTED",
        "REJECTED",
        "METADATA_REJECTED",
        "INVALID_BINARY"
    };

    private static Version ParseKnownVersion(string value, string source)
    {
        var normalized = value.Trim();
        var parts = normalized.Split('.');
        if (parts.Length is < 2 or > 3 ||
            parts.Any(static part => !int.TryParse(part, out var parsed) || parsed < 0))
        {
            throw new InvalidOperationException(
                $"{source} '{value}' is not a numeric Apple marketing version with two or three parts. Resolve the incompatible remote version before automatic selection.");
        }

        return parts.Length == 2
            ? new Version(int.Parse(parts[0]), int.Parse(parts[1]), 0)
            : new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static Version ParseResolvedPatternVersion(string value, string pattern)
    {
        var resolved = ParseKnownVersion(value, $"resolved Apple version for pattern '{pattern}'");
        if (!VersionPatternStepper.CanRepresent(pattern, resolved.ToString(3)))
            throw new InvalidOperationException($"Apple version pattern '{pattern}' resolved to incompatible version '{resolved}'.");
        return resolved;
    }

    private static Version? Highest(IEnumerable<Version> versions)
        => versions.OrderByDescending(static version => version).FirstOrDefault();

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record StoreVersionEvidence(Version Version, string? State);
}

internal sealed class AppleReleaseMarketingVersionResolution
{
    public string Pattern { get; set; } = string.Empty;

    public string MarketingVersion { get; set; } = string.Empty;

    public string? HighestRemoteMarketingVersion { get; set; }

    public bool ReusedUnreleasedMarketingVersion { get; set; }
}
