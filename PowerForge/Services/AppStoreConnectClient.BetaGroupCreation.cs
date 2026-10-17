namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Creates a TestFlight group with an explicit audience and no public invitation link.
    /// This does not invite testers or change existing group memberships.
    /// </summary>
    public Task<AppStoreConnectBetaGroupInfo> CreateBetaGroupAsync(
        string appId,
        string name,
        bool isInternalGroup,
        bool hasAccessToAllBuilds = false,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        RequireValue(name, nameof(name), "Beta group name");
        var attributes = new Dictionary<string, object>
        {
            ["name"] = name.Trim(),
            ["isInternalGroup"] = isInternalGroup,
            ["hasAccessToAllBuilds"] = hasAccessToAllBuilds
        };
        if (!isInternalGroup)
            attributes["publicLinkEnabled"] = false;
        var body = new
        {
            data = new
            {
                type = "betaGroups",
                attributes,
                relationships = new { app = new { data = new { type = "apps", id = appId.Trim() } } }
            }
        };
        return PostSingleAsync("betaGroups", body, ParseBetaGroup, cancellationToken);
    }
}
