using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>Uses NuGet's verifier and structured signature metadata to validate one package.</summary>
    /// <param name="packagePath">Exact package file.</param>
    /// <param name="requireAuthorSignature">Require an author primary signature.</param>
    /// <param name="authorFingerprints">Optional allowed SHA-256 author certificate fingerprints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>NuGet verifier process evidence; throws for unmet author identity requirements.</returns>
    public async Task<ProcessRunResult> VerifyPackageAsync(
        string packagePath, bool requireAuthorSignature = false,
        string[]? authorFingerprints = null, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(packagePath);
        var result = await _processRunner.RunAsync(new ProcessRunRequest(
            _dotNetExecutable, Path.GetDirectoryName(path)!, new[] { "nuget", "verify", "--all", path },
            _defaultTimeout), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded) return result;

        using var reader = new PackageArchiveReader(path);
        var signature = await reader.GetPrimarySignatureAsync(cancellationToken).ConfigureAwait(false);
        if (signature is null) throw new InvalidOperationException($"Package '{path}' is unsigned.");
        if ((requireAuthorSignature || authorFingerprints?.Length > 0) && signature is not AuthorPrimarySignature)
            throw new InvalidOperationException($"Package '{path}' requires an author signature.");
        if (authorFingerprints?.Length > 0)
        {
            using var sha = SHA256.Create();
            var certificate = signature.SignerInfo.Certificate
                ?? throw new InvalidOperationException($"Package '{path}' has no signer certificate.");
            var fingerprint = BitConverter.ToString(sha.ComputeHash(certificate.RawData)).Replace("-", string.Empty);
            if (!authorFingerprints.Any(value => string.Equals(value.Replace(" ", string.Empty), fingerprint, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Package '{path}' does not have an allowed author signer.");
        }
        return result;
    }
}
