using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishReleaseArtifactVerifierTests
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
    public void Verify_RejectsAbbreviatedExpectedWorkflowCommit()
    {
        using var fixture = new ReleaseFixture();
        var request = fixture.CreateRequest();
        request.ExpectedSourceRevision = fixture.SourceRevision[..12];

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("full valid expected source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsAbbreviatedManifestSourceRevision()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteManifestWithSourceRevision(fixture.SourceRevision[..12]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("full valid source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsFullSha1AsPrefixOfSha256ManifestRevision()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteManifestWithSourceRevision(fixture.SourceRevision + new string('b', 24));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("workflow commit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_AcceptsFullAndRejectsAbbreviatedSha256WorkflowCommit()
    {
        using var fixture = new ReleaseFixture();
        string sourceRevision = new string('c', 64);
        fixture.WriteManifestWithSourceRevision(sourceRevision);
        var fullRequest = fixture.CreateRequest();
        fullRequest.ExpectedSourceRevision = sourceRevision;
        var abbreviatedRequest = fixture.CreateRequest();
        abbreviatedRequest.ExpectedSourceRevision = sourceRevision[..20];

        Assert.Equal(sourceRevision, fixture.CreateVerifier().Verify(fullRequest).SourceRevision);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(abbreviatedRequest));
        Assert.Contains("full valid expected source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Verify_RejectsInstallerWithoutPrepareFromTarget()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfigurationWithoutDefaults(new
        {
            Installers = new[]
            {
                new { Id = "Test.MSI", Authoring = ReleaseFixture.AuthoringIdentity, Sign = new { Enabled = true, Thumbprint } }
            }
        });

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("PrepareFromTarget", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Verify_RejectsAChangedArtifactBeforeOpeningTheMsiDatabase()
    {
        using var fixture = new ReleaseFixture();
        File.AppendAllText(fixture.ArtifactPath, "changed");
        var packageRead = false;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier(() => packageRead = true).Verify(fixture.CreateRequest()));

        Assert.Contains("checksum manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(packageRead);
    }

    [Theory]
    [InlineData("Other", "net8.0", "win-x64", "Portable")]
    [InlineData("Service", "net9.0", "win-x64", "Portable")]
    [InlineData("Service", "net8.0", "win-arm64", "Portable")]
    [InlineData("Service", "net8.0", "win-x64", "FrameworkDependent")]
    public void Verify_RejectsManifestDimensionsOutsideTheConfiguredInstallerPlan(
        string target,
        string framework,
        string runtime,
        string style)
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(ReleaseFixture.ConfigurationWithInstallerPlan());
        fixture.WriteManifest((target, framework, runtime, style));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("configured installer plan", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_AcceptsManifestDimensionsWithinTheConfiguredInstallerPlan()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(ReleaseFixture.ConfigurationWithInstallerPlan());

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("Test.MSI", result.InstallerId);
    }

    [Fact]
    public void Verify_RejectsConfiguredAuthoredVersionWhenDynamicVersioningIsDisabled()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(ReleaseFixture.ConfigurationWithAuthoredVersion("2.0.0", dynamicVersioning: false));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("configured ProductVersion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_AllowsManifestVersionWhenDynamicVersioningIsEnabled()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(ReleaseFixture.ConfigurationWithAuthoredVersion("2.0.0", dynamicVersioning: true));

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void GetRelativePathViaUri_PreservesCrossVolumeRootedPaths()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string target = Path.GetFullPath(@"D:\release\SyncSE.msi");

        string result = DotNetPublishReleaseArtifactVerifier.GetRelativePathViaUri(@"C:\source", target);

        Assert.Equal(target, result, StringComparer.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void GetRelativePathViaUri_PreservesCrossShareUncPaths()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string target = @"\\server\release-share\SyncSE.msi";

        string result = DotNetPublishReleaseArtifactVerifier.GetRelativePathViaUri(
            @"\\server\source-share\repo",
            target);

        Assert.Equal(target, result, StringComparer.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(result));
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
        fixture.WriteConfiguration(new
        {
            Targets = new[]
            {
                new
                {
                    Name = "Service",
                    Publish = new
                    {
                        Frameworks = new[] { "net8.0", "net10.0" },
                        Runtimes = new[] { "win-x64" },
                        Styles = new[] { "Portable" }
                    }
                }
            },
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    PrepareFromTarget = "Service",
                    Authoring = ReleaseFixture.AuthoringIdentity,
                    Sign = new { Enabled = true, Thumbprint }
                }
            }
        });
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
              "Targets": [
                {
                  "Name": "Service",
                  "Publish": { "Framework": "net8.0", "Runtimes": [ "win-x64" ], "Style": "Portable" },
                },
              ],
              "Installers": [
                {
                  "Id": "Test.MSI",
                  "PrepareFromTarget": "Service",
                  "Authoring": {
                    "Product": {
                      "Name": "Test Product",
                      "Manufacturer": "Evotec",
                      "Version": "1.2.3",
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

    [Fact]
    public void Verify_SelectsOneMsiFromAMultiOutputInstallerEntry()
    {
        using var fixture = new ReleaseFixture();
        const string secondArtifact = "Artifacts/Test-Secondary-1.2.3.msi";
        fixture.CreateArtifact(secondArtifact, 17);
        fixture.WriteSingleEntryManifest("Artifacts/Test-1.2.3.msi", secondArtifact);
        var request = fixture.CreateRequest();

        Assert.Throws<InvalidDataException>(() => fixture.CreateVerifier().Verify(request));

        request.ArtifactPath = secondArtifact;
        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.Root, secondArtifact)), result.ArtifactPath);
    }

    [Fact]
    public void Verify_RespectsConfiguredOutsideRootOutputPolicy()
    {
        using var fixture = new ReleaseFixture();
        string relativeArtifact = "../" + Guid.NewGuid().ToString("N") + ".msi";
        fixture.CreateArtifact(relativeArtifact, 23);
        fixture.WriteSingleEntryManifest(relativeArtifact);

        InvalidDataException rejected = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        Assert.Contains("outside", rejected.Message, StringComparison.OrdinalIgnoreCase);

        fixture.WriteConfiguration(new
        {
            DotNet = new { AllowOutputOutsideProjectRoot = true },
            Installers = new[]
            {
                new { Id = "Test.MSI", Authoring = ReleaseFixture.AuthoringIdentity, Sign = new { Enabled = true, Thumbprint } }
            }
        });
        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.Root, relativeArtifact)), result.ArtifactPath);
    }

    [Fact]
    public void Verify_AcceptsRootedOutputWhenOutsideRootPolicyIsEnabled()
    {
        using var fixture = new ReleaseFixture();
        string rootedArtifact = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.Outside",
            Guid.NewGuid().ToString("N") + ".msi");
        fixture.CreateArtifact(rootedArtifact, 29);
        fixture.WriteSingleEntryManifest(rootedArtifact);

        InvalidDataException rejected = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        Assert.Contains("relative", rejected.Message, StringComparison.OrdinalIgnoreCase);

        fixture.WriteConfiguration(new
        {
            DotNet = new { AllowOutputOutsideProjectRoot = true },
            Installers = new[]
            {
                new { Id = "Test.MSI", Authoring = ReleaseFixture.AuthoringIdentity, Sign = new { Enabled = true, Thumbprint } }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal(Path.GetFullPath(rootedArtifact), result.ArtifactPath);
    }

    [Fact]
    public void Verify_AcceptsSubjectSelectedReleaseCertificate()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    Authoring = ReleaseFixture.AuthoringIdentity,
                    Sign = new { Enabled = true, SubjectName = "CN=Test Publisher" }
                }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier(new string('B', 40))
            .Verify(fixture.CreateRequest());

        Assert.Equal("CN=Test Publisher", result.SignerSubject);
    }

    [Fact]
    public void Verify_AcceptsAutomaticReleaseCertificateSelection()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Installers = new[]
            {
                new { Id = "Test.MSI", Authoring = ReleaseFixture.AuthoringIdentity, Sign = new { Enabled = true } }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier(new string('B', 40))
            .Verify(fixture.CreateRequest());

        Assert.Equal(new string('B', 40), result.SignerThumbprint);
    }

    [Fact]
    public void Verify_AppliesEffectiveProfileAndSigningOverrides()
    {
        const string overrideThumbprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Profiles = new[]
            {
                new { Name = "default", Default = true, Targets = new[] { "Default" } },
                new { Name = "release", Default = false, Targets = new[] { "Release" } }
            },
            Targets = new[]
            {
                new { Name = "Default", Publish = new { Framework = "net8.0", Runtimes = new[] { "win-x64" }, Style = "Portable" } },
                new { Name = "Release", Publish = new { Framework = "net8.0", Runtimes = new[] { "win-x64" }, Style = "Portable" } }
            },
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    PrepareFromTarget = "Release",
                    Authoring = ReleaseFixture.AuthoringIdentity,
                    Sign = new { Enabled = false, Thumbprint }
                }
            }
        });
        fixture.WriteManifest(("Release", "net8.0", "win-x64", "Portable"));
        var request = fixture.CreateRequest();
        request.Profile = "release";
        request.SignThumbprint = overrideThumbprint;

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier(overrideThumbprint).Verify(request);

        Assert.Equal(overrideThumbprint, result.SignerThumbprint);
    }

    [Fact]
    public void Verify_ExplicitSigningProfileAndSubjectOverrideInlineSigningIdentity()
    {
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
                    Sign = new { Enabled = false, Thumbprint = new string('C', 40) }
                }
            }
        });
        var request = fixture.CreateRequest();
        request.SignProfile = "release";
        request.SignSubjectName = "CN=Test Publisher";

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier(new string('B', 40)).Verify(request);

        Assert.Equal("CN=Test Publisher", result.SignerSubject);
        Assert.Equal(new string('B', 40), result.SignerThumbprint);
    }

    [Fact]
    public void Verify_AppliesSigningEnableOverrideUsedByReleaseBuild()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    Authoring = ReleaseFixture.AuthoringIdentity,
                    Sign = new { Enabled = false, Thumbprint }
                }
            }
        });
        var request = fixture.CreateRequest();
        request.EnableSigning = true;

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Thumbprint, result.SignerThumbprint);
    }

    [Fact]
    public void Verify_ExplicitNoSignWinsOverSignerIdentityOverrides()
    {
        using var fixture = new ReleaseFixture();
        var request = fixture.CreateRequest();
        request.EnableSigning = false;
        request.SignThumbprint = Thumbprint;
        request.SignSubjectName = "CN=Test Publisher";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => fixture.CreateVerifier().Verify(request));

        Assert.Contains("signing must be enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_AcceptsInlineUnifiedReleaseConfiguration()
    {
        using var fixture = new ReleaseFixture();
        fixture.WriteConfiguration(new
        {
            Tools = new
            {
                DotNetPublish = new
                {
                    Installers = new[]
                    {
                        new
                        {
                            Id = "Test.MSI",
                            Authoring = ReleaseFixture.AuthoringIdentity,
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }
        });

        DotNetPublishReleaseArtifact result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("Test.MSI", result.InstallerId);
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private readonly List<string> _outsideArtifacts = new();

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
                Version = "1.2.3",
                UpgradeCode = "{22222222-2222-2222-2222-222222222222}"
            }
        };

        internal static object ConfigurationWithInstallerPlan() => new
        {
            DotNet = new { Runtimes = new[] { "win-x64" } },
            Targets = new[]
            {
                new
                {
                    Name = "Service",
                    Publish = new
                    {
                        Framework = "net8.0",
                        Runtimes = new[] { "win-x64" },
                        Styles = new[] { "Portable" }
                    }
                }
            },
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    PrepareFromTarget = "Service",
                    Runtimes = new[] { "win-x64" },
                    Frameworks = new[] { "net8.0" },
                    Styles = new[] { "Portable" },
                    Authoring = AuthoringIdentity,
                    Sign = new { Enabled = true, Thumbprint }
                }
            }
        };

        internal static object ConfigurationWithAuthoredVersion(string version, bool dynamicVersioning) => new
        {
            Installers = new[]
            {
                new
                {
                    Id = "Test.MSI",
                    Authoring = new
                    {
                        Product = new
                        {
                            Name = "Test Product",
                            Manufacturer = "Evotec",
                            Version = version,
                            UpgradeCode = "{22222222-2222-2222-2222-222222222222}"
                        }
                    },
                    Versioning = new { Enabled = dynamicVersioning },
                    Sign = new { Enabled = true, Thumbprint }
                }
            }
        };

        internal string Root { get; }
        internal string ArtifactPath { get; }
        internal string ManifestPath { get; }
        internal string ChecksumsPath { get; }
        internal string ConfigurationPath { get; }
        internal string Digest { get; }
        internal string SourceRevision { get; } = new string('a', 40);

        internal void WriteConfiguration(object configuration)
        {
            JsonNode root = JsonSerializer.SerializeToNode(configuration)!;
            JsonObject spec = root["Tools"]?["DotNetPublish"] as JsonObject ?? root.AsObject();
            if (spec["Targets"] is null)
            {
                spec["Targets"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = "Service",
                        ["Publish"] = new JsonObject
                        {
                            ["Framework"] = "net8.0",
                            ["Runtimes"] = new JsonArray("win-x64"),
                            ["Style"] = "Portable"
                        }
                    }
                };
            }
            if (spec["Installers"] is JsonArray installers)
            {
                foreach (JsonObject installer in installers.OfType<JsonObject>())
                    installer["PrepareFromTarget"] ??= "Service";
            }
            File.WriteAllText(ConfigurationPath, root.ToJsonString());
        }

        internal void WriteConfigurationWithoutDefaults(object configuration) =>
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
            RefreshChecksums(["Artifacts/Test-1.2.3.msi"]);
        }

        internal void WriteManifestWithSourceRevision(string sourceRevision)
        {
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Installer",
                    InstallerId = "Test.MSI",
                    Target = "Service",
                    Framework = "net8.0",
                    Runtime = "win-x64",
                    Style = "Portable",
                    OutputFiles = new[] { "Artifacts/Test-1.2.3.msi" },
                    SignedFiles = 1,
                    SourceRevision = sourceRevision,
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
                }
            }));
            RefreshChecksums(["Artifacts/Test-1.2.3.msi"]);
        }

        internal void WriteSingleEntryManifest(params string[] outputFiles)
        {
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Installer",
                    InstallerId = "Test.MSI",
                    Target = "Service",
                    Framework = "net8.0",
                    Runtime = "win-x64",
                    Style = "Portable",
                    OutputFiles = outputFiles,
                    SignedFiles = outputFiles.Length,
                    SourceRevision,
                    SourceDirty = false,
                    PackageMetadata = outputFiles.Select(path => new
                    {
                        Path = path,
                        ProductName = "Test Product",
                        Manufacturer = "Evotec",
                        ProductVersion = "1.2.3",
                        ProductCode = "{11111111-1111-1111-1111-111111111111}",
                        UpgradeCode = "{22222222-2222-2222-2222-222222222222}",
                        ReadError = (string?)null
                    }).ToArray()
                }
            }));
            RefreshChecksums(outputFiles);
        }

        internal string CreateArtifact(string relativePath, byte seed)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Enumerable.Range(0, 512).Select(value => (byte)(value + seed)).ToArray());
            string rootPrefix = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                _outsideArtifacts.Add(path);
            return path;
        }

        private void RefreshChecksums(IReadOnlyList<string> outputFiles)
        {
            string manifestDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ManifestPath)));
            List<string> checksums = outputFiles.Select(relativePath =>
            {
                string path = Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return $"{digest.ToLowerInvariant()} *{relativePath.Replace('\\', '/')}";
            }).ToList();
            checksums.Add($"{manifestDigest.ToLowerInvariant()} *manifest.json");
            File.WriteAllLines(ChecksumsPath, checksums);
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
                _ => ReadPackageMetadata(),
                _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Test Publisher",
                    thumbprint));

        internal DotNetPublishReleaseArtifactVerifier CreateVerifier(Action onReadPackage) =>
            new(
                _ =>
                {
                    onReadPackage();
                    return ReadPackageMetadata();
                },
                _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Test Publisher",
                    Thumbprint));

        internal DotNetPublishMsiPackageMetadata ReadPackageMetadata() => new()
        {
            Path = ArtifactPath,
            ProductName = "Test Product",
            Manufacturer = "Evotec",
            ProductVersion = "1.2.3",
            ProductCode = "{11111111-1111-1111-1111-111111111111}",
            UpgradeCode = "{22222222-2222-2222-2222-222222222222}"
        };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            foreach (string path in _outsideArtifacts)
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
