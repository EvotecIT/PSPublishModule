using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class AppStoreConnectCommandSupport
{
    internal static AppStoreConnectApiCredential CreateCredential(
        string issuerId,
        string keyId,
        string? privateKey,
        string? privateKeyPath,
        int tokenLifetimeMinutes)
    {
        var resolvedPrivateKey = ResolvePrivateKey(privateKey, privateKeyPath);
        return new AppStoreConnectApiCredential
        {
            IssuerId = issuerId.Trim(),
            KeyId = keyId.Trim(),
            PrivateKey = resolvedPrivateKey,
            TokenLifetime = TimeSpan.FromMinutes(tokenLifetimeMinutes <= 0 ? 15 : tokenLifetimeMinutes)
        };
    }

    internal static string? ResolvePrivateKeyPath(SessionState sessionState, string? privateKeyPath)
        => string.IsNullOrWhiteSpace(privateKeyPath)
            ? null
            : sessionState.Path.GetUnresolvedProviderPathFromPSPath(privateKeyPath!);

    private static string ResolvePrivateKey(string? privateKey, string? privateKeyPath)
    {
        if (!string.IsNullOrWhiteSpace(privateKey))
            return privateKey!.Trim();

        if (!string.IsNullOrWhiteSpace(privateKeyPath))
            return File.ReadAllText(privateKeyPath!).Trim();

        throw new ArgumentException("PrivateKey or PrivateKeyPath is required.");
    }
}
