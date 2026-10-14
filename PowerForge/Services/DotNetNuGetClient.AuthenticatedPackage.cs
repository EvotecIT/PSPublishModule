using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>Authenticates and hashes one immutable archive, without trusting extracted-cache metadata.</summary>
    internal static Task<AuthenticatedPackage> AuthenticatePackageAsync(
        string packagePath, string expectedId, string expectedVersion, string[] authorFingerprints,
        CancellationToken cancellationToken = default)
        => AuthenticatedPackage.CreateAsync(packagePath, expectedId, expectedVersion, authorFingerprints, cancellationToken);

    /// <summary>Content authority captured only after NuGet verifies the same archive reader.</summary>
    internal sealed class AuthenticatedPackage
    {
        private const int MaximumArchiveBytes = 512 * 1024 * 1024;
        private readonly Dictionary<string, string> _entryHashes;
        private readonly byte[] _archive;

        private AuthenticatedPackage(string id, string version, Dictionary<string, string> entryHashes, byte[] archive)
        {
            Id = id;
            Version = version;
            _entryHashes = entryHashes;
            _archive = archive;
        }

        internal string Id { get; }
        internal string Version { get; }

        internal bool Matches(string entryPath, string sha256)
            => _entryHashes.TryGetValue(entryPath, out var authenticatedHash) &&
               authenticatedHash.Equals(sha256, StringComparison.OrdinalIgnoreCase);

        /// <summary>Installs the already-authenticated snapshot into a fresh compiler-owned v3 package root.</summary>
        internal async Task InstallAsync(string packageRoot, CancellationToken cancellationToken)
        {
            var resolver = new VersionFolderPathResolver(packageRoot, isLowercase: true);
            var identity = new PackageIdentity(Id, NuGetVersion.Parse(Version));
            if (Directory.Exists(resolver.GetInstallPath(identity.Id, identity.Version)))
                throw new InvalidOperationException($"Authenticated package '{identity}' requires a fresh extraction directory.");
            var context = new PackageExtractionContext(PackageSaveMode.Defaultv3, XmlDocFileSaveMode.None,
                ClientPolicyContext.GetClientPolicy(NullSettings.Instance, NuGet.Common.NullLogger.Instance), NuGet.Common.NullLogger.Instance);
            var installed = await PackageExtractor.InstallFromSourceAsync("authenticated compiler snapshot", identity,
                async destination =>
                {
                    using var input = new MemoryStream(_archive, writable: false);
                    await input.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                }, resolver, context, cancellationToken, Guid.Empty).ConfigureAwait(false);
            if (!installed)
                throw new InvalidOperationException($"Authenticated package '{identity}' was not freshly extracted.");
            VerifyInstalled(packageRoot);
        }

        /// <summary>Rejects staged input drift before and after the controlled SDK invocation.</summary>
        internal void VerifyInstalled(string packageRoot)
        {
            var directory = Path.Combine(packageRoot, Id.ToLowerInvariant(), Version.ToLowerInvariant());
            // NuGet extraction can rename the root nuspec while SDK download extraction
            // preserves its signed archive spelling. Only those exact names share authority.
            var entries = new Dictionary<string, string>(_entryHashes, StringComparer.Ordinal);
            var nuspecs = _entryHashes.Where(static entry => entry.Key.IndexOf('/') < 0 &&
                entry.Key.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nuspecs.Length != 1)
                throw new InvalidOperationException($"Authenticated package '{Id}/{Version}' requires one signed root nuspec.");
            var nuspec = nuspecs[0];
            var normalizedNuspec = Id.ToLowerInvariant() + ".nuspec";
            if (nuspec.Key != normalizedNuspec)
            {
                if (entries.ContainsKey(normalizedNuspec))
                    throw new InvalidOperationException($"Authenticated package '{Id}/{Version}' has conflicting signed nuspec paths.");
                entries.Add(normalizedNuspec, nuspec.Value);
            }
            var stagedNuspecCount = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = FrameworkCompatibility.GetRelativePath(directory, path).Replace('\\', '/');
                if (relative == nuspec.Key || relative == normalizedNuspec) stagedNuspecCount++;
                if (relative == ".nupkg.metadata" ||
                    relative == Id.ToLowerInvariant() + "." + Version.ToLowerInvariant() + ".nupkg.sha512" ||
                    relative == Id.ToLowerInvariant() + "." + Version.ToLowerInvariant() + ".nupkg") continue;
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                if (!entries.TryGetValue(relative, out var expected) ||
                    !expected.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Authenticated package '{Id}/{Version}' staged entry '{relative}' changed.");
            }
            if (stagedNuspecCount != 1)
                throw new InvalidOperationException($"Authenticated package '{Id}/{Version}' requires exactly one staged root nuspec; metadata is absent or ambiguous.");
            foreach (var entry in entries.Keys.Where(static path => path == ".signature.p7s" ||
                         path.StartsWith("runtimes/", StringComparison.Ordinal) || path.StartsWith("tools/", StringComparison.Ordinal) ||
                         path.StartsWith("build/", StringComparison.Ordinal) || path.StartsWith("Sdk/", StringComparison.Ordinal) ||
                         path.StartsWith("analyzers/", StringComparison.Ordinal)))
                if (!File.Exists(Path.Combine(directory, entry.Replace('/', Path.DirectorySeparatorChar))))
                    throw new InvalidOperationException($"Authenticated package '{Id}/{Version}' staged entry '{entry}' disappeared.");
        }

        internal static async Task<AuthenticatedPackage> CreateAsync(
            string packagePath, string expectedId, string expectedVersion, string[] authorFingerprints,
            CancellationToken cancellationToken)
        {
            if (authorFingerprints is null || authorFingerprints.Length == 0)
                throw new ArgumentException("Authenticated package content requires explicit author certificate fingerprints.", nameof(authorFingerprints));
            // A cache directory can be replaced or modified by its caller. Capture once, then
            // verify signature, package identity and entry bytes through this same read-only reader.
            using var snapshot = new MemoryStream();
            using (var input = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (snapshot.Length + read > MaximumArchiveBytes)
                        throw new InvalidOperationException($"Package '{packagePath}' exceeds the authenticated archive size limit.");
                    snapshot.Write(buffer, 0, read);
                }
            }
            using var immutable = new MemoryStream(snapshot.GetBuffer(), 0, checked((int)snapshot.Length), writable: false, publiclyVisible: false);
            using var reader = new PackageArchiveReader(immutable, leaveStreamOpen: true);
            var signature = await reader.GetPrimarySignatureAsync(cancellationToken).ConfigureAwait(false);
            if (signature is not AuthorPrimarySignature)
                throw new InvalidOperationException($"Package '{packagePath}' requires an authenticated author primary signature.");
            var fingerprint = signature.GetSigningCertificateFingerprint(NuGet.Common.HashAlgorithmName.SHA256);
            if (!authorFingerprints.Contains(fingerprint, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Package '{packagePath}' does not have an allowed authenticated author signer.");

            var verifier = new PackageSignatureVerifier(new ISignatureVerificationProvider[]
            {
                new IntegrityVerificationProvider(),
                new SignatureTrustAndValidityVerificationProvider(),
                new AllowListVerificationProvider(authorFingerprints.Select(static value =>
                    (VerificationAllowListEntry)new CertificateHashAllowListEntry(VerificationTarget.Author,
                        SignaturePlacement.PrimarySignature, value, NuGet.Common.HashAlgorithmName.SHA256)).ToArray(),
                    requireNonEmptyAllowList: true)
            });
            var verification = await verifier.VerifySignaturesAsync(reader,
                SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(), cancellationToken).ConfigureAwait(false);
            if (!verification.IsValid)
                throw new InvalidOperationException($"Package '{packagePath}' failed NuGet signature verification: " +
                    string.Join(" ", verification.Results.SelectMany(static result => result.Issues).Select(static issue => issue.Message)));

            var identity = await reader.GetIdentityAsync(cancellationToken).ConfigureAwait(false);
            if (!identity.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase) ||
                !identity.Version.ToNormalizedString().Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Authenticated package '{identity}' does not match selected package '{expectedId}/{expectedVersion}'.");

            var entryHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in reader.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var entry = reader.GetStream(path);
                using var sha = SHA256.Create();
                var hash = BitConverter.ToString(sha.ComputeHash(entry)).Replace("-", string.Empty).ToLowerInvariant();
                if (entryHashes.ContainsKey(path))
                    throw new InvalidOperationException($"Authenticated package '{packagePath}' repeats archive entry '{path}'.");
                entryHashes.Add(path, hash);
            }
            return new AuthenticatedPackage(identity.Id, identity.Version.ToNormalizedString(), entryHashes, snapshot.ToArray());
        }
    }
}
