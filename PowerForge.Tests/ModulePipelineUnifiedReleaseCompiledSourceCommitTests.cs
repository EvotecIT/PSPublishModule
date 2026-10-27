using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilation")]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Run_ReusedCompiledStagingPreservesOriginalGitHubSourceAndLegacyUnknown(bool unified, bool environmentReferences)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var sourceRoot = Path.Combine(root.FullName, "source");
        var staging = Path.Combine(root.FullName, "staging");
        string pfxVariable = "PF_CHECKPOINT_PFX_" + Guid.NewGuid().ToString("N");
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(sourceRoot, moduleName, "1.0.0");
            InitializeModuleSourceGit(sourceRoot);
            var originalCommit = ReleaseSourceCommit.Capture(sourceRoot);
            Assert.NotNull(originalCommit);
            var signing = CreateCompiledSourceCheckpointSigning(root.FullName);
            if (environmentReferences)
            {
                Environment.SetEnvironmentVariable(pfxVariable, Convert.ToBase64String(File.ReadAllBytes(signing.CertificatePFXPath!)));
                Environment.SetEnvironmentVariable(pfxVariable + "_PASSWORD", signing.CertificatePFXPassword);
                signing.CertificatePFXBase64EnvironmentVariable = pfxVariable;
                signing.CertificatePFXPasswordEnvironmentVariable = pfxVariable + "_PASSWORD";
                signing.CertificatePFXPath = null;
                signing.CertificatePFXPassword = null;
            }
            var requests = new List<GitHubReleasePublishRequest>();
            var hosted = new FakeHostedOperations(new List<string>())
            {
                BinaryDependencyValidation = _ => { },
                ModulePublishAction = (publish, plan) =>
                    requests.Add(ModulePublisher.CreateGitHubReleaseRequest(publish, plan, Array.Empty<string>()))
            };
            var runner = CreateRunner(hosted,
                (_, _, _) => throw new Xunit.Sdk.XunitException("No package build was configured."),
                request =>
                {
                    requests.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true, ReleaseCreationSucceeded = true, ReleaseId = 42 };
                });
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Signing = signing } },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration { Enabled = true, Path = Path.Combine(root.FullName, "artifacts") }
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true, Destination = PublishDestination.GitHub,
                        UserName = "EvotecIT", RepositoryName = moduleName, ApiKey = "fixture-token"
                    }
                }
            };
            if (unified)
                segments.Add(new ConfigurationReleaseSegment { Configuration = new ReleaseConfiguration() });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName, SourcePath = sourceRoot, Version = "1.0.0", StagingPath = staging, KeepStaging = true,
                    PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                    {
                        Enabled = true, Mode = PowerShellCompilationMode.Hybrid, TargetFramework = "net10.0",
                        AllowUnreviewedDependencies = true
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };

            var built = runner.Run(spec);
            var compiled = Assert.IsType<PowerShellModuleCompilationResult>(built.PowerShellCompilationResult);
            Assert.Equal(originalCommit, compiled.SourceCommitSha);
            var originalAssembly = File.ReadAllBytes(compiled.AssemblyPath);
            RunModuleSourceGit(sourceRoot, "commit", "--quiet", "--allow-empty", "-m", "Advance checkout after compilation");
            Assert.NotEqual(originalCommit, ReleaseSourceCommit.Capture(sourceRoot));
            spec.Build.ReuseStaging = true;
            spec.Build.SkipDotNetBuild = true;

            var reused = runner.Run(spec);
            Assert.Equal(originalCommit, reused.Plan.SourceCommitSha);
            Assert.Equal(originalCommit, reused.PowerShellCompilationResult!.SourceCommitSha);
            Assert.Equal(originalAssembly, File.ReadAllBytes(reused.PowerShellCompilationResult.AssemblyPath));
            Assert.Equal(2, requests.Count);
            Assert.All(requests, request =>
            {
                Assert.Equal(originalCommit, request.Commitish);
                Assert.Equal(originalCommit, request.ExpectedTagCommitSha);
            });

            // Emulate a correctly authenticated legacy checkpoint that predates source context.
            var checkpointPath = Path.Combine(staging, moduleName + ".powerforge-module-compilation.json");
            var checkpoint = JsonNode.Parse(File.ReadAllText(checkpointPath))!.AsObject();
            Assert.True(checkpoint.Remove("sourceCommitSha"));
            File.WriteAllText(checkpointPath, checkpoint.ToJsonString());
            var signature = PowerShellCompilationEvidenceAuthenticator.Sign(File.ReadAllBytes(checkpointPath), signing);
            File.WriteAllBytes(Path.Combine(staging, moduleName + ".powerforge-module-compilation.p7s"), signature.Signature);

            var legacy = runner.Run(spec);
            Assert.Null(legacy.Plan.SourceCommitSha);
            Assert.Null(legacy.PowerShellCompilationResult!.SourceCommitSha);
            Assert.Equal(originalAssembly, File.ReadAllBytes(legacy.PowerShellCompilationResult.AssemblyPath));
            Assert.Equal(3, requests.Count);
            Assert.Null(requests[2].Commitish);
            Assert.Null(requests[2].ExpectedTagCommitSha);
        }
        finally
        {
            Environment.SetEnvironmentVariable(pfxVariable, null);
            Environment.SetEnvironmentVariable(pfxVariable + "_PASSWORD", null);
            DeleteModuleSourceFixture(new DirectoryInfo(sourceRoot));
            if (Directory.Exists(root.FullName)) root.Delete(recursive: true);
        }
    }

    private static SigningOptionsConfiguration CreateCompiledSourceCheckpointSigning(string root)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Compilation source context fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        const string password = "compilation-source-fixture";
        var path = Path.Combine(root, "compilation-source-authority.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return new SigningOptionsConfiguration { CertificatePFXPath = path, CertificatePFXPassword = password, AllowedCertificateThumbprints = [certificate.Thumbprint] };
    }
}
