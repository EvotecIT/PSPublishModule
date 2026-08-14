using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Run_SignedPackedArtifactEmitsEvidenceFromFinalLayout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string sourceRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string manifestPath = Path.Combine(root.FullName, moduleName + ".psd1");
            _ = PowerForgeModuleSourceAttestationWriter.Write(
                manifestPath,
                moduleName,
                "1.0.0",
                sourceRevision,
                sourceDirty: false);
            File.WriteAllText(
                Path.Combine(root.FullName, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName),
                "{\"moduleName\":\"TestModule\",\"version\":\"1.0.0\",\"commit\":\"" + sourceRevision + "\",\"sourceDirty\":false}");
            var hostedOperations = new FakeHostedOperations { AutoSuccessfulSigningResult = true };
            var runner = CreateRunner(hostedOperations);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Packed");
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            string evidencePath = Assert.Single(artefact.EvidencePaths);
            Assert.True(File.Exists(evidencePath));
            Assert.Equal(4, hostedOperations.SignCalls);
            string reboundAttestation = Assert.Single(hostedOperations.LastPackageFilePaths);
            Assert.EndsWith("PowerForge.ReleaseProvenance.psd1", reboundAttestation, StringComparison.OrdinalIgnoreCase);
            Assert.True(hostedOperations.LastSigningOptions?.OverwriteSigned);
            Assert.DoesNotContain("Modules", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
            using var archive = System.IO.Compression.ZipFile.OpenRead(artefact.OutputPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.psd1");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SignedPackedArtifactWithoutAttestationStillRunsFinalLayoutSigning()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var hostedOperations = new FakeHostedOperations { AutoSuccessfulSigningResult = true };
            var runner = CreateRunner(hostedOperations);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Packed");
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            Assert.Empty(artefact.EvidencePaths);
            Assert.Equal(3, hostedOperations.SignCalls);
            Assert.Equal(3, hostedOperations.SigningRootPaths.Count);
            Assert.NotEqual(hostedOperations.SigningRootPaths[0], hostedOperations.SigningRootPaths[1]);
            Assert.DoesNotContain("Modules", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(artefact.OutputPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SignedGitHubPackedArtifactGeneratesAndVerifiesCompleteReleaseEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string revision = RunGit(root.FullName, "rev-parse", "HEAD");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
            var runner = CreateRunner(hostedOperations);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Packed");
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            EnableGitHubPublish(spec, moduleName);

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            string evidencePath = Assert.Single(artefact.EvidencePaths);
            Assert.True(File.Exists(evidencePath));
            Assert.Contains(revision, File.ReadAllText(evidencePath), StringComparison.OrdinalIgnoreCase);
            ArtefactBuildResult publishedArtefact = Assert.Single(hostedOperations.LastPublishedArtefacts);
            Assert.Equal(artefact.OutputPath, publishedArtefact.OutputPath);
            Assert.Equal(new[] { evidencePath }, publishedArtefact.EvidencePaths);
            using var archive = System.IO.Compression.ZipFile.OpenRead(artefact.OutputPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.psd1");
            Assert.Contains(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.json");
            archive.Dispose();

            string checksumPath = ModulePublisher.WriteDirectGitHubChecksumCatalog(
                result.ArtefactResults,
                new[] { artefact.OutputPath, evidencePath });
            const string publisherThumbprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var verifier = new PowerForgeReleaseArtifactVerifier(
                _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Publisher",
                    publisherThumbprint),
                _ => "1.0.0");
            PowerForgeReleaseArtifactEvidence verified = verifier.Verify(
                new PowerForgeReleaseArtifactVerificationRequest
                {
                    Kind = PowerForgeReleaseArtifactKind.PowerShellModule,
                    ArtifactId = moduleName,
                    ProjectRoot = Path.GetDirectoryName(artefact.OutputPath)!,
                    ArtifactPath = artefact.OutputPath,
                    ChecksumsPath = checksumPath,
                    ExpectedSourceRevision = revision,
                    ExpectedVersion = "1.0.0",
                    SignThumbprint = publisherThumbprint,
                    SigningEvidencePath = evidencePath
                });
            Assert.Equal(revision, verified.SourceRevision, ignoreCase: true);
            Assert.Equal("valid", verified.SignatureStatus);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Plan_SignedGitHubPackedArtifactRequiresResolvedCleanGitSource(bool initializeGit, bool makeDirty)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            if (initializeGit)
            {
                RunGit(root.FullName, "init", "--quiet");
                RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
                RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
                RunGit(root.FullName, "add", ".");
                RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            }
            if (makeDirty)
                File.WriteAllText(Path.Combine(root.FullName, "dirty.txt"), "dirty");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CreateRunner(new FakeHostedOperations()).Plan(spec));

            Assert.Contains("resolved clean Git checkout", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_SignedGitHubPackedArtifactRejectsIgnoredPackagedSourceInput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "ignored-input.json\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            File.WriteAllText(Path.Combine(root.FullName, "ignored-input.json"), "{\"configuration\":true}");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CreateRunner(new FakeHostedOperations()).Plan(spec));

            Assert.Contains("resolved clean Git checkout", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SignedGitHubPackedArtifactRejectsPostPlanTrackedSourceMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string sourceModule = Path.Combine(root.FullName, moduleName + ".psm1");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                SigningCallStarted = call =>
                {
                    if (call == 1)
                        File.AppendAllText(sourceModule, Environment.NewLine + "# changed during build");
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Contains("source changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SignedGitHubPackedArtifactRejectsPostPlanIgnoredSourceMutation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "ignored-during-build.json\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                SigningCallStarted = call =>
                {
                    if (call == 1)
                        File.WriteAllText(Path.Combine(root.FullName, "ignored-during-build.json"), "{}");
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Contains("source changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_MultiplePackedArtifactsAggregateEverySigningResult()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var hostedOperations = new FakeHostedOperations();
            hostedOperations.SigningResults.Enqueue(CreateSigningResult("stage.psd1", signedNew: 1));
            hostedOperations.SigningResults.Enqueue(CreateSigningResult("packed-one.psd1", signedNew: 1));
            hostedOperations.SigningResults.Enqueue(CreateSigningResult("packed-one.psm1", signedNew: 1));
            hostedOperations.SigningResults.Enqueue(CreateSigningResult(
                "packed-two.psd1",
                signedNew: 0,
                alreadySignedOther: 1,
                vendorPath: "vendor.dll"));
            hostedOperations.SigningResults.Enqueue(CreateSigningResult("packed-two.psm1", signedNew: 0));
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "PackedOne"));
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root.FullName, "Artefacts", "PackedTwo"),
                        ArtefactName = moduleName + "-second.zip"
                    }
                }
            }).ToArray();

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            ModuleSigningResult aggregate = Assert.IsType<ModuleSigningResult>(result.SigningResult);
            Assert.Equal(5, hostedOperations.SignCalls);
            Assert.Equal(5, aggregate.TotalAfterExclude);
            Assert.Equal(3, aggregate.SignedNew);
            Assert.Equal(1, aggregate.AlreadySignedOther);
            Assert.Equal(
                new[] { "stage.psd1", "packed-one.psd1", "packed-one.psm1", "packed-two.psd1", "packed-two.psm1" },
                aggregate.VerifiedFilePaths);
            ModuleSigningPreservedSignature vendor = Assert.Single(aggregate.PreservedThirdPartySignatures);
            Assert.Equal("vendor.dll", vendor.FilePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ModuleSigningResult CreateSigningResult(
        string verifiedPath,
        int signedNew,
        int alreadySignedOther = 0,
        string? vendorPath = null)
        => new()
        {
            TotalMatched = 1,
            TotalAfterExclude = 1,
            SignedNew = signedNew,
            AlreadySignedOther = alreadySignedOther,
            CertificateThumbprint = "ABC123",
            VerifiedFilePaths = new[] { verifiedPath },
            PreservedThirdPartySignatures = vendorPath is null
                ? Array.Empty<ModuleSigningPreservedSignature>()
                : new[]
                {
                    new ModuleSigningPreservedSignature
                    {
                        FilePath = vendorPath,
                        Subject = "CN=Vendor",
                        Thumbprint = "DEF456"
                    }
                }
        };

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed: {error}");
        return output.Trim();
    }

    private static ModulePipelineSpec CreateSignedPackedSpec(string sourcePath, string moduleName, string outputRoot)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0"
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationOptionsSegment
                {
                    Options = new ConfigurationOptions
                    {
                        Signing = new SigningOptionsConfiguration { CertificateThumbprint = "ABC123" }
                    }
                },
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration { SignMerged = true }
                },
                new ConfigurationInformationSegment
                {
                    Configuration = new InformationConfiguration
                    {
                        IncludeRoot = new[] { "*.psd1", "*.psm1", "PowerForge.ReleaseProvenance.json" }
                    }
                },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = outputRoot,
                        ArtefactName = moduleName + ".zip"
                    }
                }
            }
        };
    }

    private static void EnableGitHubPublish(ModulePipelineSpec spec, string moduleName)
    {
        spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
        {
            new ConfigurationPublishSegment
            {
                Configuration = new PublishConfiguration
                {
                    Destination = PublishDestination.GitHub,
                    Enabled = true,
                    UserName = "EvotecIT",
                    RepositoryName = moduleName,
                    ApiKey = "test-token"
                }
            }
        }).ToArray();
    }

    [Fact]
    public void SignBuiltModuleOutput_UsesInjectedHostedOperations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var hostedOperations = new FakeHostedOperations
        {
            NextSigningResult = new ModuleSigningResult { SignedNew = 2, Attempted = 2 }
        };

        try
        {
            WriteSigningFixture(root.FullName, "TestModule");
            var runner = CreateRunner(hostedOperations);

            var result = InvokeSignBuiltModuleOutput(
                runner,
                "TestModule",
                root.FullName,
                new SigningOptionsConfiguration
                {
                    CertificateThumbprint = "ABC123",
                    IncludeInternals = false
                },
                includeScriptFolders: false);

            Assert.Same(hostedOperations.NextSigningResult, result);
            Assert.Equal(1, hostedOperations.SignCalls);
            Assert.Contains("*.ps1", hostedOperations.LastIncludePatterns, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Modules", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
            AssertPackageFiles(
                root.FullName,
                hostedOperations.LastPackageFilePaths,
                "TestModule.psd1",
                "TestModule.psm1",
                Path.Combine("Lib", "Default", "Binary.dll"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SignBuiltModuleOutput_IncludesPackagedScriptFoldersForUnmergedModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteSigningFixture(root.FullName, moduleName);
            var hostedOperations = new FakeHostedOperations();

            _ = InvokeSignBuiltModuleOutput(
                CreateRunner(hostedOperations),
                moduleName,
                root.FullName,
                new SigningOptionsConfiguration { CertificateThumbprint = "ABC123" },
                includeScriptFolders: true);

            AssertPackageFiles(
                root.FullName,
                hostedOperations.LastPackageFilePaths,
                "TestModule.psd1",
                "TestModule.psm1",
                Path.Combine("Lib", "Default", "Binary.dll"),
                Path.Combine("Public", "Get-Test.ps1"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SignBuiltModuleOutput_CustomInternalsRemainPackagedButHonorSigningOptOut()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteSigningFixture(root.FullName, moduleName);
            WriteInternalTool(root.FullName);
            var hostedOperations = new FakeHostedOperations();

            _ = InvokeSignBuiltModuleOutput(
                CreateRunner(hostedOperations),
                moduleName,
                root.FullName,
                new SigningOptionsConfiguration
                {
                    CertificateThumbprint = "ABC123",
                    IncludeExe = true,
                    IncludeInternals = false
                },
                includeScriptFolders: false,
                delivery: CreateDelivery());

            AssertPackageFiles(
                root.FullName,
                hostedOperations.LastPackageFilePaths,
                "TestModule.psd1",
                "TestModule.psm1",
                Path.Combine("Lib", "Default", "Binary.dll"),
                Path.Combine("Payload", "Tools", "helper.exe"));
            Assert.Contains("Payload", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SignBuiltModuleOutput_CustomInternalsSigningOptInRemovesDeliveryExclusion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteSigningFixture(root.FullName, moduleName);
            WriteInternalTool(root.FullName);
            var hostedOperations = new FakeHostedOperations();

            _ = InvokeSignBuiltModuleOutput(
                CreateRunner(hostedOperations),
                moduleName,
                root.FullName,
                new SigningOptionsConfiguration
                {
                    CertificateThumbprint = "ABC123",
                    IncludeExe = true,
                    IncludeInternals = true
                },
                includeScriptFolders: false,
                delivery: CreateDelivery());

            Assert.DoesNotContain("Payload", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(
                hostedOperations.LastPackageFilePaths,
                path => path.EndsWith(Path.Combine("Payload", "Tools", "helper.exe"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ModulePipelineRunner CreateRunner(FakeHostedOperations hostedOperations)
        => new(
            new NullLogger(),
            new ThrowingPowerShellRunner(),
            new FakeMetadataProvider(),
            hostedOperations);

    private static DeliveryOptionsConfiguration CreateDelivery()
        => new() { Enable = true, InternalsPath = "Payload" };

    private static void WriteInternalTool(string rootPath)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "Payload", "Tools"));
        File.WriteAllText(Path.Combine(rootPath, "Payload", "Tools", "helper.exe"), "binary");
    }

    private static ModuleSigningResult InvokeSignBuiltModuleOutput(
        ModulePipelineRunner runner,
        string moduleName,
        string rootPath,
        SigningOptionsConfiguration signing,
        bool includeScriptFolders,
        DeliveryOptionsConfiguration? delivery = null)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("SignBuiltModuleOutput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "SignBuiltModuleOutput method signature may have changed.");
        return (ModuleSigningResult)method!.Invoke(
            runner,
            new object?[] { moduleName, rootPath, signing, null, delivery, includeScriptFolders })!;
    }

    private static void WriteSigningFixture(string rootPath, string moduleName)
    {
        WriteMinimalModule(rootPath, moduleName, "1.0.0");
        Directory.CreateDirectory(Path.Combine(rootPath, "Lib", "Default"));
        File.WriteAllText(Path.Combine(rootPath, "Lib", "Default", "Binary.dll"), "binary");
        Directory.CreateDirectory(Path.Combine(rootPath, "Public"));
        File.WriteAllText(Path.Combine(rootPath, "Public", "Get-Test.ps1"), "function Get-Test { }");
        Directory.CreateDirectory(Path.Combine(rootPath, "Docs"));
        File.WriteAllText(Path.Combine(rootPath, "Docs", "BuildOnly.ps1"), "throw 'not packaged'");
    }

    private static void AssertPackageFiles(string rootPath, string[] actualPaths, params string[] expectedRelativePaths)
    {
        var expected = expectedRelativePaths
            .Select(path => Path.GetFullPath(Path.Combine(rootPath, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = actualPaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expected, actual);
    }
}
