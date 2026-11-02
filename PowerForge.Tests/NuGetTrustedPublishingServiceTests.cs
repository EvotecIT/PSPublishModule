using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using static PowerForge.DotNetRepositoryReleaseService;

namespace PowerForge.Tests;

public sealed class NuGetTrustedPublishingServiceTests : IDisposable
{
    private const string Commit = "0123456789012345678901234567890123456789";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.NuGetTrusted.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<byte[]> _uploads = new();
    private readonly List<string> _registry = new();
    private readonly VerificationRunner _runner = new();
    private readonly RSA _key = RSA.Create(2048);
    private readonly X509Certificate2 _certificate;
    private readonly HttpClient _http;
    private Func<HttpRequestMessage, HttpResponseMessage> _response = _ => new(HttpStatusCode.NotFound);

    public NuGetTrustedPublishingServiceTests()
    {
        Directory.CreateDirectory(_root);
        var request = new CertificateRequest("CN=Ephemeral package test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _http = new HttpClient(new RegistryHandler(r => { _registry.Add(r.RequestUri!.AbsolutePath); return _response(r); }));
    }

    private string Fingerprint => Convert.ToHexString(SHA256.HashData(_certificate.RawData));

    [Fact]
    public async Task Stage_records_signed_identity_hash_source_and_symbols_without_changing_bytes()
    {
        var primary = await Package("Example");
        var symbols = await Package("Example", symbols: true);
        var bytes = File.ReadAllBytes(primary);
        var release = Release(primary, symbols);
        var manifest = await Service().StageAsync(release);

        var verified = await Service().VerifyAsync(manifest, Commit, [Fingerprint]);

        Assert.Equal(Commit, verified.SourceCommitSha);
        Assert.Equal(2, verified.Assets.Length);
        var asset = Assert.Single(verified.Assets, a => a.FileName.EndsWith(".nupkg", StringComparison.Ordinal));
        Assert.Equal("Example", asset.Id);
        Assert.Equal("1.2.3", asset.Version);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), asset.Sha256);
        Assert.Equal(Fingerprint, asset.AuthorCertificateFingerprint);
        Assert.Equal(bytes, File.ReadAllBytes(primary));
        Assert.All(_runner.Requests, r => Assert.Equal(new[] { "nuget", "verify", "--all" }, r.Arguments.Take(3)));
        Assert.Empty(_uploads);
    }

    [Theory]
    [InlineData("hash", "SHA256")]
    [InlineData("signer", "allowed")]
    [InlineData("commit", "source commit")]
    [InlineData("traversal", "Invalid")]
    [InlineData("duplicate", "duplicate")]
    [InlineData("identity", "identity")]
    [InlineData("extra-package", "missing from")]
    public async Task Verify_rejects_unqualified_assets_before_publication(string variant, string expectedError)
    {
        var package = await Package("Example");
        var manifest = await Service().StageAsync(Release(package));
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        var asset = json["assets"]![0]!;
        switch (variant)
        {
            case "hash": File.AppendAllText(package, "changed"); break;
            case "signer": asset["authorCertificateFingerprint"] = new string('A', 64); break;
            case "commit": json["sourceCommitSha"] = new string('A', 40); break;
            case "traversal": asset["fileName"] = "../outside.nupkg"; break;
            case "duplicate": json["assets"]!.AsArray().Add(asset.DeepClone()); break;
            case "identity": asset["id"] = "Other"; break;
            case "extra-package": File.Copy(package, Path.Combine(_root, "Unlisted.1.2.3.nupkg")); break;
        }
        File.WriteAllText(manifest, json.ToJsonString());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PublishAsync(manifest, Commit, [Fingerprint]));

        Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_registry);
        Assert.Empty(_uploads);
    }

    [Fact]
    public async Task Retry_accepts_repository_countersigned_bytes_with_the_same_author_without_a_key_or_upload()
    {
        var package = await Package("Example");
        var manifest = await Service().StageAsync(Release(package));
        var published = await RepositoryCountersign(package);
        Assert.NotEqual(SHA256.HashData(File.ReadAllBytes(package)), SHA256.HashData(published));
        _response = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(published) };

        await Service().PublishAsync(manifest, Commit, [Fingerprint], "POWERFORGE_TEST_KEY_NOT_SET_" + Guid.NewGuid().ToString("N"));

        Assert.Single(_registry);
        Assert.Empty(_uploads);
    }

    [Theory]
    [InlineData("signer")]
    [InlineData("identity")]
    [InlineData("unsigned")]
    [InlineData("unavailable")]
    public async Task Registry_conflict_in_a_later_package_blocks_the_whole_release(string conflict)
    {
        var first = await Package("First");
        var second = await Package("Second");
        var manifest = await Service().StageAsync(Release(first, second));
        var other = await Package(conflict == "identity" ? "Other" : "Second", suffix: "remote", unsigned: conflict == "unsigned",
            differentSigner: conflict == "signer");
        // Remote fixture is outside the downloaded asset set.
        File.Move(other, other + ".fixture");
        var remoteBytes = File.ReadAllBytes(other + ".fixture");
        _response = r => r.RequestUri!.AbsolutePath.Contains("/first/") ? new(HttpStatusCode.NotFound)
            : conflict == "unavailable" ? new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(remoteBytes) };

        await Assert.ThrowsAnyAsync<Exception>(() => Service().PublishAsync(manifest, Commit, [Fingerprint]));

        Assert.Equal(2, _registry.Count);
        Assert.Empty(_uploads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_version_uploads_the_verified_file_and_reads_back_the_author(bool replaceOriginalDuringRegistryPreflight)
    {
        var package = await Package("Example");
        var manifest = await Service().StageAsync(Release(package));
        var bytes = File.ReadAllBytes(package);
        byte[]? replacementBytes = null;
        if (replaceOriginalDuringRegistryPreflight)
        {
            var replacement = await Package("Example", suffix: "-replacement", payload: "different build");
            replacementBytes = File.ReadAllBytes(replacement);
            File.Move(replacement, replacement + ".fixture");
        }
        _response = _ =>
        {
            if (_uploads.Count == 0)
            {
                if (replacementBytes is not null) File.WriteAllBytes(package, replacementBytes);
                return new(HttpStatusCode.NotFound);
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        };
        var env = "POWERFORGE_TEST_SHORT_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(env, "ephemeral-test-key");
        try { await Service().PublishAsync(manifest, Commit, [Fingerprint], env); }
        finally { Environment.SetEnvironmentVariable(env, null); }

        Assert.Equal(bytes, Assert.Single(_uploads));
        Assert.Equal(2, _registry.Count);
        Assert.All(_registry, p => Assert.Equal("/v3-flatcontainer/example/1.2.3/example.1.2.3.nupkg", p));
    }

    [Fact]
    public async Task Stage_rejects_an_author_signature_without_a_timestamp()
    {
        var package = await Package("Example", timestamp: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().StageAsync(Release(package)));

        Assert.Contains("timestamp", error.Message);
        Assert.False(File.Exists(Path.Combine(_root, NuGetTrustedPublishingService.ManifestFileName)));
    }

    private NuGetTrustedPublishingService Service() => new(new NullLogger(), new DotNetNuGetClient(_runner),
        new DotNetRepositoryReleaseService(new NullLogger(), null, null, (path, key, source, skipDuplicate, suppressSymbols, workingDirectory) =>
        {
            Assert.Equal("ephemeral-test-key", key);
            Assert.Equal("https://api.nuget.org/v3/index.json", source);
            Assert.False(skipDuplicate);
            _uploads.Add(File.ReadAllBytes(path));
            return new PackagePushResult { Outcome = PackagePushOutcome.Published };
        }), _http);

    private static DotNetRepositoryReleaseResult Release(params string[] paths) => new()
    {
        Success = true, SourceCommitSha = Commit,
        Projects = paths.Select(p => new DotNetRepositoryProjectResult
        {
            IsPackable = true,
            Packages = p.EndsWith(".nupkg", StringComparison.Ordinal) ? [p] : [],
            SymbolPackages = p.EndsWith(".snupkg", StringComparison.Ordinal) ? [p] : []
        }).ToList()
    };

    private async Task<string> Package(string id, bool symbols = false, string suffix = "", bool unsigned = false,
        bool differentSigner = false, bool timestamp = true, string payload = "fixture payload")
    {
        var path = Path.Combine(_root, id + ".1.2.3" + suffix + (symbols ? ".snupkg" : ".nupkg"));
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                writer.Write($"<package><metadata><id>{id}</id><version>1.2.3</version><authors>Example</authors><description>Fixture</description></metadata></package>");
            using (var writer = new StreamWriter(zip.CreateEntry(symbols ? "lib/net10.0/Example.pdb" : "lib/net10.0/Example.dll").Open())) writer.Write(payload);
        }
        if (unsigned) { File.WriteAllBytes(path, input.ToArray()); return path; }
        input.Position = 0;
        using var output = new MemoryStream();
        using var package = new SignedPackageArchive(input, output);
        var hash = await package.GetArchiveHashAsync(NuGet.Common.HashAlgorithmName.SHA256, CancellationToken.None);
        var content = new SignatureContent(SigningSpecifications.V1, NuGet.Common.HashAlgorithmName.SHA256, Convert.ToBase64String(hash));
        using var otherKey = RSA.Create(2048);
        using var otherCertificate = new CertificateRequest("CN=Other ephemeral signer", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signature = await new X509SignatureProvider(null).CreatePrimarySignatureAsync(
            new AuthorSignPackageRequest(differentSigner ? otherCertificate : _certificate, NuGet.Common.HashAlgorithmName.SHA256),
            content, NuGet.Common.NullLogger.Instance, CancellationToken.None);
        if (timestamp) AddTimestamp(signature.SignedCms);
        using var signatureStream = new MemoryStream(signature.SignedCms.Encode());
        await package.AddSignatureAsync(signatureStream, CancellationToken.None);
        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private void AddTimestamp(SignedCms author)
    {
        var info = new AsnWriter(AsnEncodingRules.DER);
        info.PushSequence();
        info.WriteInteger(1);
        info.WriteObjectIdentifier("1.2.3.4");
        info.PushSequence();
        info.PushSequence();
        info.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
        info.WriteNull();
        info.PopSequence();
        info.WriteOctetString(SHA256.HashData(author.SignerInfos[0].GetSignature()));
        info.PopSequence();
        info.WriteInteger(1);
        info.WriteGeneralizedTime(DateTimeOffset.UtcNow);
        info.PopSequence();
        var stamp = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
        var certificateId = new AsnWriter(AsnEncodingRules.DER);
        certificateId.PushSequence();
        certificateId.PushSequence();
        certificateId.PushSequence();
        certificateId.WriteOctetString(SHA256.HashData(_certificate.RawData));
        certificateId.PopSequence();
        certificateId.PopSequence();
        certificateId.PopSequence();
        var signer = new CmsSigner(_certificate);
        signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), certificateId.Encode()));
        stamp.ComputeSignature(signer, silent: true);
        author.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.14"), stamp.Encode()));
    }

    private async Task<byte[]> RepositoryCountersign(string path)
    {
        using var reader = new PackageArchiveReader(path);
        var primary = (await reader.GetPrimarySignatureAsync(CancellationToken.None))!;
        var request = new RepositorySignPackageRequest(_certificate, NuGet.Common.HashAlgorithmName.SHA256,
            NuGet.Common.HashAlgorithmName.SHA256, new Uri("https://api.nuget.org/v3/index.json"), ["Example"]);
        var countersigned = await new X509SignatureProvider(null).CreateRepositoryCountersignatureAsync(request, primary,
            NuGet.Common.NullLogger.Instance, CancellationToken.None);
        using var input = File.OpenRead(path);
        using var output = new MemoryStream();
        using var package = new SignedPackageArchive(input, output);
        await package.RemoveSignatureAsync(CancellationToken.None);
        using var unsigned = new MemoryStream(output.ToArray());
        using var final = new MemoryStream();
        using var archive = new SignedPackageArchive(unsigned, final);
        using var signature = new MemoryStream(countersigned.GetBytes());
        await archive.AddSignatureAsync(signature, CancellationToken.None);
        return final.ToArray();
    }

    // Trust-store validation belongs to dotnet nuget verify. Synthetic roots stay out of system stores;
    // this transport verifies the real CMS and archive digest before returning that process success.
    private sealed class VerificationRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = new();
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            using var package = new PackageArchiveReader(request.Arguments.Last());
            var signature = await package.GetPrimarySignatureAsync(cancellationToken);
            if (signature is null) return new ProcessRunResult(1, "", "unsigned", "dotnet", TimeSpan.Zero, false);
            signature.SignedCms.CheckSignature(verifySignatureOnly: true);
            await package.ValidateIntegrityAsync(signature.SignatureContent, cancellationToken);
            return new ProcessRunResult(0, "", "", "dotnet", TimeSpan.Zero, false);
        }
    }

    private sealed class RegistryHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    public void Dispose()
    {
        _http.Dispose();
        _certificate.Dispose();
        _key.Dispose();
        Directory.Delete(_root, true);
    }
}
