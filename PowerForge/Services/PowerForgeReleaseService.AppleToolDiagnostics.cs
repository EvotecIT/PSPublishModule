namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string[] AppleToolCredentials(PowerForgeAppleReleasePlan plan)
    {
        var values = new List<string?>
        {
            plan.AppStoreConnectApiKeyPath,
            plan.AppStoreConnectApiKeyId,
            plan.AppStoreConnectApiIssuerId
        };
        foreach (var name in new[]
                 {
                     "APP_STORE_CONNECT_PRIVATE_KEY_PATH", "APP_STORE_CONNECT_PRIVATE_KEY", "APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_ISSUER_ID",
                     "ASC_PRIVATE_KEY_PATH", "ASC_PRIVATE_KEY", "ASC_KEY_ID", "ASC_ISSUER_ID"
                 })
            values.Add(Environment.GetEnvironmentVariable(name));
        return values.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim()).Distinct(StringComparer.Ordinal)
            .OrderByDescending(static value => value.Length).ToArray();
    }
}
