using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class DotNetPublishReleaseArtifactVerifierTests
{
    private const string Thumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Verify_ReturnsFactsOnlyAfterAllReleaseEvidenceMatches()
    {
        using var fixture = new ReleaseFixture();
        var verifier = fixture.CreateVerifier();

        DotNetPublishReleaseArtifact result = verifier.Verify(fixture.CreateRequest());

        Assert.Equal("Test.MSI", result.InstallerId);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("{11111111-1111-1111-1111-111111111111}", result.ProductCode);
        Assert.Equal("{22222222-2222-2222-2222-222222222222}", result.UpgradeCode);
        Assert.Equal(fixture.SourceRevision, result.SourceRevision);
        Assert.Equal(Thumbprint, result.SignerThumbprint);
        Assert.Equal(fixture.Digest, result.Sha256);
    }

    [Fact]
    public void Verify_RejectsManifestFromAnotherWorkflowCommit()
    {
        using var fixture = new ReleaseFixture();
        var request = fixture.CreateRequest();
        request.ExpectedSourceRevision = new string('b', 40);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(request));

        Assert.Contains("workflow commit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RequiresCallerBoundSourceRevision()
    {
        using var fixture = new ReleaseFixture();
        var request = fixture.CreateRequest();
        request.ExpectedSourceRevision = string.Empty;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(request));

        Assert.Contains("expected source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsAChangedArtifactEvenWhenManifestIdentityStillMatches()
    {
        using var fixture = new ReleaseFixture();
        File.AppendAllText(fixture.ArtifactPath, "changed");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("checksum manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsSignerOutsideTheConfiguredReleaseIdentity()
    {
        using var fixture = new ReleaseFixture();
        var verifier = fixture.CreateVerifier(new string('B', 40));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => verifier.Verify(fixture.CreateRequest()));

        Assert.Contains("configured release certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_UsesMatrixSelectorsToIdentifyOneInstallerArtifact()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteManifest(
            ("Service", "net8.0", "win-x64", "Portable"),
            ("Service", "net10.0", "win-x64", "Portable"));
        var request = fixture.CreateRequest();

        InvalidDataException ambiguous = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(request));
        Assert.Contains("matrix builds", ambiguous.Message, StringComparison.OrdinalIgnoreCase);

        request.Target = "Service";
        request.Runtime = "win-x64";
        request.Framework = "net10.0";
        request.Style = "Portable";
        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(request);

        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void Verify_AppliesSigningProfileOverrides()
    {
        const string overrideThumbprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            SigningProfiles = new Dictionary<string, object>
            {
                ["release"] = new { Enabled = true, Thumbprint }
            },
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    Authoring = ReleaseFixture.AuthoringIdentity,
                    SignProfile = "release",
                    SignOverrides = new { Thumbprint = overrideThumbprint }
                }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier(overrideThumbprint)
            .Verify(fixture.CreateRequest());

        Assert.Equal(overrideThumbprint, result.SignerThumbprint);
    }

    [Fact]
    public void Verify_AcceptsPublishConfigurationCommentsAndTrailingCommas()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(
            $$"""
            {
              // PowerForge publish configuration supports comments.
              "Installers": [
                {
                  "Id": "Test.MSI",
                  "Authoring": {
                    "Product": {
                      "Name": "Test Product",
                      "Manufacturer": "Evotec",
                      "UpgradeCode": "{22222222-2222-2222-2222-222222222222}",
                    },
                  },
                  "Sign": { "Enabled": true, "Thumbprint": "{{Thumbprint}}", },
                },
              ],
            }
            """);

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("Test.MSI", result.InstallerId);
    }

    [Fact]
    public void Verify_RejectsManifestWhoseOwnChecksumChanged()
    {
        using var fixture = new ReleaseFixture();
        File.AppendAllText(fixture.ManifestPath, Environment.NewLine);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("manifest SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_SupportsHandAuthoredInstallerProjects()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    InstallerProjectPath = "Installer/Test.wixproj",
                    Sign = new { Enabled = true, Thumbprint }
                }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("Test Product", result.ProductName);
        Assert.Equal("{22222222-2222-2222-2222-222222222222}", result.UpgradeCode);
    }

    private sealed class ReleaseFixture : IDisposable
    {
        internal ReleaseFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "Artifacts"));
            ArtifactPath = Path.Combine(Root, "Artifacts", "Test-1.2.3.msi");
            File.WriteAllBytes(ArtifactPath, Enumerable.Range(0, 512).Select(value => (byte)value).ToArray());
            Digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ArtifactPath)));
            ManifestPath = Path.Combine(Root, "manifest.json");
            ChecksumsPath = Path.Combine(Root, "SHA256SUMS.txt");
            ConfigurationPath = Path.Combine(Root, "powerforge.dotnetpublish.json");

            WriteConfiguration(new
            {
                Installers = new[]
                {
                    new
                    {
                        Id = "Test.MSI",
                        Authoring = AuthoringIdentity,
                        Sign = new { Enabled = true, Thumbprint }
                    }
                }
            });
            WriteManifest(("Service", "net8.0", "win-x64", "Portable"));
        }

        internal static object AuthoringIdentity => new
        {
            Product = new
            {
                Name = "Test Product",
                Manufacturer = "Evotec",
                UpgradeCode = "{22222222-2222-2222-2222-222222222222}"
            }
        };

        internal string Root { get; }
        internal string ArtifactPath { get; }
        internal string ManifestPath { get; }
        internal string ChecksumsPath { get; }
        internal string ConfigurationPath { get; }
        internal string Digest { get; }
        internal string SourceRevision { get; } = new string('a', 40);

        internal void WriteConfiguration(object configuration) =>
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(configuration));

        internal void WriteConfiguration(string configuration) =>
            File.WriteAllText(ConfigurationPath, configuration);

        internal void WriteManifest(params (string Target, string Framework, string Runtime, string Style)[] combinations)
        {
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(combinations.Select(combination => new
            {
                Category = "Installer",
                InstallerId = "Test.MSI",
                combination.Target,
                combination.Framework,
                combination.Runtime,
                combination.Style,
                OutputFiles = new[] { "Artifacts/Test-1.2.3.msi" },
                SignedFiles = 1,
                SourceRevision,
                SourceDirty = false,
                PackageMetadata = new[]
                {
                    new
                    {
                        Path = "Artifacts/Test-1.2.3.msi",
                        ProductName = "Test Product",
                        Manufacturer = "Evotec",
                        ProductVersion = "1.2.3",
                        ProductCode = "{11111111-1111-1111-1111-111111111111}",
                        UpgradeCode = "{22222222-2222-2222-2222-222222222222}",
                        ReadError = (string?)null
                    }
                }
            })));
            RefreshChecksums();
        }

        private void RefreshChecksums()
        {
            string manifestDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ManifestPath)));
            File.WriteAllLines(ChecksumsPath,
            [
                $"{Digest.ToLowerInvariant()} *Artifacts/Test-1.2.3.msi",
                $"{manifestDigest.ToLowerInvariant()} *manifest.json"
            ]);
        }

        internal DotNetPublishReleaseArtifactVerificationRequest CreateRequest() => new()
        {
            ProjectRoot = Root,
            ManifestPath = ManifestPath,
            ChecksumsPath = ChecksumsPath,
            ConfigurationPath = ConfigurationPath,
            InstallerId = "Test.MSI",
            ExpectedSourceRevision = SourceRevision
        };

        internal DotNetPublishReleaseArtifactVerifier CreateVerifier(string thumbprint = Thumbprint) =>
            new(
                _ => new DotNetPublishMsiPackageMetadata
                {
                    Path = ArtifactPath,
                    ProductName = "Test Product",
                    Manufacturer = "Evotec",
                    ProductVersion = "1.2.3",
                    ProductCode = "{11111111-1111-1111-1111-111111111111}",
                    UpgradeCode = "{22222222-2222-2222-2222-222222222222}"
                },
                _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Test Publisher",
                    thumbprint));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
