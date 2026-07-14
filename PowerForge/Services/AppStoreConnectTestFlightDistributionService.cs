namespace PowerForge;

/// <summary>
/// Distributes processed App Store Connect builds through TestFlight beta groups.
/// </summary>
public sealed class AppStoreConnectTestFlightDistributionService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a TestFlight distribution service.
    /// </summary>
    public AppStoreConnectTestFlightDistributionService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Adds a build to beta groups and optionally ensures testers are present in those groups.
    /// </summary>
    public async Task<AppStoreConnectTestFlightDistributionResult> DistributeAsync(
        AppStoreConnectTestFlightDistributionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new ArgumentException("AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VersionString))
            throw new ArgumentException("VersionString is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BuildNumber))
            throw new ArgumentException("BuildNumber is required.", nameof(request));

        var build = (await _client.GetBuildsAsync(
            request.AppId,
            request.BuildNumber,
            limit: 20,
            marketingVersion: request.VersionString,
            platform: request.Platform,
            cancellationToken: cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Build '{request.BuildNumber}' was not found for app '{request.AppId}', version '{request.VersionString}', and platform '{request.Platform}'.");

        if (request.RequireValidBuild)
            ValidateBuildIsDistributable(build, request.BuildNumber);

        var groups = await ResolveGroupsAsync(request, cancellationToken).ConfigureAwait(false);
        if (groups.Length == 0)
            throw new InvalidOperationException("At least one beta group id or beta group name is required.");

        var messages = new List<string>();
        foreach (var group in groups)
        {
            if (group.IsInternalGroup == true && group.HasAccessToAllBuilds == true)
            {
                messages.Add($"Internal beta group '{group.Name ?? group.Id}' has access to all builds; skipped explicit build assignment.");
                continue;
            }

            await _client.AddBuildsToBetaGroupAsync(group.Id, new[] { build.Id }, cancellationToken).ConfigureAwait(false);
            messages.Add($"Added build '{request.BuildNumber}' to beta group '{group.Name ?? group.Id}'.");
        }

        var testers = new List<AppStoreConnectBetaTesterInfo>();
        foreach (var testerSpec in request.Testers ?? Array.Empty<AppStoreConnectBetaTesterSpec>())
        {
            var resolution = await ResolveTesterAsync(testerSpec, request.CreateMissingTesters, groups, cancellationToken).ConfigureAwait(false);
            testers.Add(resolution.Tester);
            foreach (var group in resolution.TargetGroups)
                await _client.AddBetaTestersToBetaGroupAsync(group.Id, new[] { resolution.Tester.Id }, cancellationToken).ConfigureAwait(false);
            messages.Add($"Added beta tester '{resolution.Tester.Email ?? resolution.Tester.Id}' to {resolution.TargetGroups.Length} beta group(s).");
        }

        return new AppStoreConnectTestFlightDistributionResult
        {
            Build = build,
            BetaGroups = groups,
            Testers = testers.ToArray(),
            Messages = messages.ToArray()
        };
    }

    private async Task<AppStoreConnectBetaGroupInfo[]> ResolveGroupsAsync(
        AppStoreConnectTestFlightDistributionRequest request,
        CancellationToken cancellationToken)
    {
        var groups = new List<AppStoreConnectBetaGroupInfo>();
        foreach (var id in request.BetaGroupIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var match = (await _client.GetBetaGroupsAsync(request.AppId, limit: 200, cancellationToken: cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(group => string.Equals(group.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException($"Beta group id '{id}' was not found for app '{request.AppId}'.");
            groups.Add(match);
        }

        foreach (var name in request.BetaGroupNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var matches = await _client.GetBetaGroupsAsync(request.AppId, name.Trim(), limit: 20, cancellationToken).ConfigureAwait(false);
            if (matches.Length == 0)
                throw new InvalidOperationException($"Beta group '{name}' was not found for app '{request.AppId}'.");
            if (matches.Length > 1)
                throw new InvalidOperationException($"Multiple beta groups matched '{name}' for app '{request.AppId}'; use BetaGroupIds.");
            groups.Add(matches[0]);
        }

        return groups
            .GroupBy(static group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private async Task<(AppStoreConnectBetaTesterInfo Tester, AppStoreConnectBetaGroupInfo[] TargetGroups)> ResolveTesterAsync(
        AppStoreConnectBetaTesterSpec testerSpec,
        bool createMissing,
        AppStoreConnectBetaGroupInfo[] groups,
        CancellationToken cancellationToken)
    {
        if (testerSpec is null || string.IsNullOrWhiteSpace(testerSpec.Email))
            throw new ArgumentException("Every tester requires Email.", nameof(testerSpec));

        var existing = (await _client.GetBetaTestersAsync(testerSpec.Email.Trim(), limit: 10, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (existing is not null)
            return (existing, groups);
        if (!createMissing)
            throw new InvalidOperationException($"Beta tester '{testerSpec.Email}' was not found and CreateMissingTesters is false.");

        var targetGroups = groups
            .Where(static group => group.IsInternalGroup != true)
            .ToArray();
        if (targetGroups.Length == 0)
            throw new InvalidOperationException($"Beta tester '{testerSpec.Email}' was not found. New external testers cannot be created for internal-only TestFlight groups.");

        var created = await _client.CreateBetaTesterAsync(
            testerSpec.Email,
            testerSpec.FirstName,
            testerSpec.LastName,
            targetGroups.Select(static group => group.Id).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return (created, targetGroups);
    }

    private static void ValidateBuildIsDistributable(AppStoreConnectBuildInfo build, string buildNumber)
    {
        if (!string.Equals(build.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be distributed because processing state is '{build.ProcessingState ?? "unknown"}'.");
        if (build.Expired == true)
            throw new InvalidOperationException($"Build '{buildNumber}' cannot be distributed because it is expired.");
    }
}
