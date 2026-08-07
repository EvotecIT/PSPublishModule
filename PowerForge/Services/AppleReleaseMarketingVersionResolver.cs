namespace PowerForge;

/// <summary>
/// Resolves an Apple marketing-version pattern against local, TestFlight, and
/// App Store release trains.
/// </summary>
internal static class AppleReleaseMarketingVersionResolver
{
    internal static void ValidatePattern(string pattern, string settingName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidOperationException($"{settingName} is required.");

        try
        {
            var candidate = VersionPatternStepper.Step(pattern.Trim(), currentVersion: null);
            _ = ParseKnownVersion(candidate, $"resolved Apple version for {settingName}");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"{settingName} must follow the shared PSPublishModule X-pattern semantics and resolve to an Apple marketing version with two or three numeric parts: {exception.Message}",
                exception);
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
            .Select(static value => value.Version.Numeric)
            .Distinct()
            .ToArray();
        var highestOccupied = Highest(occupiedVersions);

        var reusableCandidates = storeEvidence
            .Where(static value => IsReusableAppStoreVersion(value.State))
            .Select(static value => value.Version)
            .Concat(testFlightVersions)
            .Concat(new[] { local })
            .Where(version => VersionPatternStepper.CanRepresent(pattern, version.Identity))
            .Where(version => !occupiedVersions.Contains(version.Numeric))
            .Where(version => highestOccupied is null || version.Numeric.CompareTo(highestOccupied) > 0)
            .GroupBy(static version => version.Numeric)
            .Select(static group => group.First())
            .OrderByDescending(static version => version.Numeric)
            .ToArray();

        var reusable = reusableCandidates.FirstOrDefault();
        var highestKnown = Highest(new[] { local.Numeric }.Concat(remoteVersions.Select(static value => value.Numeric)));
        var resolved = reusable?.Identity ?? ParseResolvedPatternVersion(
            VersionPatternStepper.Step(pattern.Trim(), highestKnown),
            pattern);

        return new AppleReleaseMarketingVersionResolution
        {
            Pattern = pattern.Trim(),
            MarketingVersion = resolved,
            HighestRemoteMarketingVersion = HighestIdentity(remoteVersions),
            ReusedUnreleasedMarketingVersion = reusable is not null
        };
    }

    private static bool IsReusableAppStoreVersion(string? state)
        => state is not null && ReusableAppStoreStates.Contains(state);

    internal static void ValidateRemoteEvidence(
        IEnumerable<AppStoreConnectVersionInfo> storeVersions,
        IEnumerable<AppStoreConnectBuildInfo> builds)
    {
        var versions = storeVersions.ToArray();
        var testFlightBuilds = builds.ToArray();
        if (versions.Any(static value => string.IsNullOrWhiteSpace(value.VersionString)))
        {
            throw new InvalidOperationException(
                "App Store Connect returned a version without a marketing-version identity. Resolve the incomplete remote version before automatic selection.");
        }

        if (testFlightBuilds.Any(static value => string.IsNullOrWhiteSpace(value.MarketingVersion)))
        {
            throw new InvalidOperationException(
                "App Store Connect returned a TestFlight build without a marketing-version identity. Resolve the incomplete remote build before automatic selection.");
        }

        ValidateRemotePlatforms(
            versions.Select(static value => value.Platform),
            "App Store version");
        ValidateRemotePlatforms(
            testFlightBuilds.Select(static value => value.Platform),
            "TestFlight build");
    }

    private static void ValidateRemotePlatforms(IEnumerable<string?> platforms, string source)
    {
        foreach (var platform in platforms)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                throw new InvalidOperationException(
                    $"App Store Connect returned a {source} without platform identity. Resolve the incomplete remote evidence before automatic selection.");
            }

            if (!SupportedRemotePlatforms.Contains(platform!.Trim()))
            {
                throw new InvalidOperationException(
                    $"App Store Connect returned unsupported {source} platform identity '{platform}'. Resolve the incompatible remote evidence before automatic selection.");
            }
        }
    }

    private static readonly HashSet<string> SupportedRemotePlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "IOS",
        "MAC_OS",
        "TV_OS",
        "VISION_OS"
    };

    private static readonly HashSet<string> ReusableAppStoreStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "PREPARE_FOR_SUBMISSION",
        "READY_FOR_REVIEW",
        "DEVELOPER_REJECTED",
        "REJECTED",
        "METADATA_REJECTED",
        "INVALID_BINARY"
    };

    private static KnownVersion ParseKnownVersion(string value, string source)
    {
        var normalized = value.Trim();
        var parts = normalized.Split('.');
        if (parts.Length is < 2 or > 3 ||
            parts.Any(static part => !int.TryParse(part, out var parsed) || parsed < 0))
        {
            throw new InvalidOperationException(
                $"{source} '{value}' is not a numeric Apple marketing version with two or three parts. Resolve the incompatible remote version before automatic selection.");
        }

        var numeric = parts.Length == 2
            ? new Version(int.Parse(parts[0]), int.Parse(parts[1]), 0)
            : new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        return new KnownVersion(numeric, normalized);
    }

    private static string ParseResolvedPatternVersion(string value, string pattern)
    {
        var resolved = ParseKnownVersion(value, $"resolved Apple version for pattern '{pattern}'");
        if (!VersionPatternStepper.CanRepresent(pattern, resolved.Identity))
            throw new InvalidOperationException($"Apple version pattern '{pattern}' resolved to incompatible version '{resolved.Identity}'.");
        return resolved.Identity;
    }

    private static Version? Highest(IEnumerable<Version> versions)
        => versions.OrderByDescending(static version => version).FirstOrDefault();

    private static string? HighestIdentity(IEnumerable<KnownVersion> versions)
        => versions.OrderByDescending(static version => version.Numeric).FirstOrDefault()?.Identity;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class StoreVersionEvidence
    {
        internal StoreVersionEvidence(KnownVersion version, string? state)
        {
            Version = version;
            State = state;
        }

        internal KnownVersion Version { get; }

        internal string? State { get; }
    }

    private sealed class KnownVersion
    {
        internal KnownVersion(Version numeric, string identity)
        {
            Numeric = numeric;
            Identity = identity;
        }

        internal Version Numeric { get; }

        internal string Identity { get; }
    }
}

internal sealed class AppleReleaseMarketingVersionResolution
{
    public string Pattern { get; set; } = string.Empty;

    public string MarketingVersion { get; set; } = string.Empty;

    public string? HighestRemoteMarketingVersion { get; set; }

    public bool ReusedUnreleasedMarketingVersion { get; set; }
}
