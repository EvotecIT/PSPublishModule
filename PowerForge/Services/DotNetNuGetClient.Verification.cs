namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>Uses NuGet's verifier and structured signature metadata to validate one package.</summary>
    /// <param name="packagePath">Exact package file.</param>
    /// <param name="requireAuthorSignature">Require an author primary signature.</param>
    /// <param name="authorFingerprints">Optional allowed SHA-256 author certificate fingerprints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>NuGet verifier process evidence; throws for unmet author identity requirements.</returns>
    public Task<ProcessRunResult> VerifyPackageAsync(
        string packagePath, bool requireAuthorSignature = false,
        string[]? authorFingerprints = null, CancellationToken cancellationToken = default)
        => NuGetReleaseVerifier.VerifySignatureAsync(packagePath, requireAuthorSignature, authorFingerprints,
            _processRunner, _dotNetExecutable, _defaultTimeout, cancellationToken);

    internal Task<NuGetReleaseManifest> VerifyReleaseAsync(string manifestPath, string sourceCommitSha,
        string[] authorFingerprints, CancellationToken cancellationToken)
        => NuGetReleaseVerifier.VerifyAsync(manifestPath, sourceCommitSha, authorFingerprints,
            _processRunner, _dotNetExecutable, _defaultTimeout, cancellationToken);
}
