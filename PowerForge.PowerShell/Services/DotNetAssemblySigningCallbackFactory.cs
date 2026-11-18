using System;

namespace PowerForge;

/// <summary>
/// Creates assembly-signing callbacks for .NET release workflows.
/// </summary>
public static class DotNetAssemblySigningCallbackFactory
{
    /// <summary>
    /// Creates a callback that validates Authenticode assembly-signing prerequisites.
    /// </summary>
    /// <param name="logger">Logger used by the signing service.</param>
    /// <returns>Assembly-signing preflight callback suitable for .NET release services.</returns>
    public static Action<DotNetReleaseBuildAssemblySigningPreflightRequest> CreatePreflight(ILogger logger)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var signingService = new AuthenticodeSigningService(logger);
        return req =>
        {
            if (req is null)
                throw new ArgumentNullException(nameof(req));

            var lookup = signingService.SelectCertificateFromStore(req.LocalStore, req.CertificateThumbprint);
            if (lookup.Certificate is null)
                throw new InvalidOperationException($"Certificate '{req.CertificateThumbprint}' not found in {req.LocalStore}\\My store.");

            signingService.EnsureSigningCommandsAvailable();

            if (!req.UnlockSigningKey)
                return;
            if (!ShouldUnlockEarly(out var skipReason))
            {
                logger.Verbose($"Skipping early signing-key unlock: {skipReason}.");
                return;
            }

            logger.Info("Unlocking the signing key now; a hardware token may ask for its PIN once.");
            var elapsed = signingService.UnlockSigningKey(lookup.Certificate, req.TimeStampServer);
            logger.Info($"Signing key ready in {DotNetRepositoryReleaseService.FormatDuration(elapsed)}.");
        };
    }

    internal const string EarlyUnlockEnvironmentVariable = "POWERFORGE_SIGNING_EARLY_UNLOCK";

    private static bool ShouldUnlockEarly(out string reason)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(EarlyUnlockEnvironmentVariable)?.Trim(), "0", StringComparison.Ordinal))
        {
            reason = EarlyUnlockEnvironmentVariable + " is 0";
            return false;
        }
        foreach (var name in new[] { "CI", "TF_BUILD", "GITHUB_ACTIONS" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value!.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"running in CI ({name})";
                return false;
            }
        }
        if (!Environment.UserInteractive)
        {
            reason = "the session is not interactive";
            return false;
        }
        // On Unix UserInteractive is always true, so require a terminal for any prompt.
        if (Environment.OSVersion.Platform != PlatformID.Win32NT && (Console.IsInputRedirected || Console.IsOutputRedirected))
        {
            reason = "no terminal is attached";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Creates a callback that signs release assemblies with Authenticode.
    /// </summary>
    /// <param name="logger">Logger used by the signing service.</param>
    /// <returns>Assembly-signing callback suitable for .NET release services.</returns>
    public static Action<DotNetReleaseBuildAssemblySigningRequest> Create(ILogger logger)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var signingService = new AuthenticodeSigningService(logger);
        return req =>
        {
            if (req is null)
                throw new ArgumentNullException(nameof(req));

            var lookup = signingService.SelectCertificateFromStore(req.LocalStore, req.CertificateThumbprint);
            if (lookup.Certificate is null)
                throw new InvalidOperationException($"Certificate '{req.CertificateThumbprint}' not found in {req.LocalStore}\\My store.");

            var files = req.FilePaths is { Length: > 0 }
                ? req.FilePaths
                : signingService.EnumerateFiles(req.ReleasePath, req.IncludePatterns);
            signingService.SignFiles(new AuthenticodeSignRequest
            {
                Certificate = lookup.Certificate,
                FilePaths = files,
                TimeStampServer = req.TimeStampServer,
                HashAlgorithm = "SHA256",
                WindowsIncludeChain = "All",
                NonWindowsIncludeChain = "WholeChain",
                OverwriteSigned = req.OverwriteSigned,
                MaxDegreeOfParallelism = req.MaxDegreeOfParallelism
            });
        };
    }
}
