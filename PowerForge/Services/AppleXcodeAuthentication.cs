namespace PowerForge;

internal static class AppleXcodeAuthentication
{
    internal static void AddArguments(
        string? appStoreConnectApiKeyPath,
        string? appStoreConnectApiKeyId,
        string? appStoreConnectApiIssuerId,
        bool allowProvisioningUpdates,
        List<string> arguments)
    {
        var keyPath = string.IsNullOrWhiteSpace(appStoreConnectApiKeyPath)
            ? null
            : Path.GetFullPath(appStoreConnectApiKeyPath!);
        var keyId = string.IsNullOrWhiteSpace(appStoreConnectApiKeyId)
            ? null
            : appStoreConnectApiKeyId!.Trim();
        var issuerId = string.IsNullOrWhiteSpace(appStoreConnectApiIssuerId)
            ? null
            : appStoreConnectApiIssuerId!.Trim();
        var configuredCount =
            (keyPath is null ? 0 : 1) +
            (keyId is null ? 0 : 1) +
            (issuerId is null ? 0 : 1);
        if (configuredCount == 0)
            return;
        if (configuredCount != 3)
        {
            throw new ArgumentException(
                "App Store Connect API-key authentication requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.");
        }
        if (!allowProvisioningUpdates)
        {
            throw new ArgumentException(
                "App Store Connect API-key authentication requires AllowProvisioningUpdates=true so xcodebuild can use the credentials.");
        }
        if (!File.Exists(keyPath))
            throw new FileNotFoundException($"App Store Connect API key file was not found: {keyPath}", keyPath);

        arguments.Add("-authenticationKeyPath");
        arguments.Add(keyPath!);
        arguments.Add("-authenticationKeyID");
        arguments.Add(keyId!);
        arguments.Add("-authenticationKeyIssuerID");
        arguments.Add(issuerId!);
    }
}
