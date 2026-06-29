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
        };
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
                NonWindowsIncludeChain = "WholeChain"
            });
        };
    }
}
