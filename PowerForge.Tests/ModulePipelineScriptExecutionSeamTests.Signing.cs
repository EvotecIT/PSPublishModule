using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Run_SignedPackedArtifactStripsPreexistingProvenanceWhenProtectionIsDisabled()
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
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
            var runner = CreateRunner(hostedOperations);
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Packed");
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            Assert.Empty(artefact.EvidencePaths);
            using var archive = System.IO.Compression.ZipFile.OpenRead(artefact.OutputPath);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.psd1");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.json");
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
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
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

    [Theory]
    [InlineData(ModulePipelineActionStage.AfterArtefacts)]
    [InlineData(ModulePipelineActionStage.BeforePublish)]
    public void Run_SignedPackedArtifactRejectsPostFinalizationActionMutation(ModulePipelineActionStage stage)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                ActionStarted = (_, context) =>
                {
                    string artefactPath = Assert.Single(context.ArtefactPaths);
                    File.AppendAllText(artefactPath, "mutated after packed artifact finalization");
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Name = "mutate finalized archive",
                        At = stage,
                        InlineScript = "# executed through the test host"
                    }
                }
            }).ToArray();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("changed after signing", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string revision = RunGit(root.FullName, "rev-parse", "HEAD");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true,
                SigningFilesCompleted = (call, files) =>
                {
                    if (call != 1)
                        return;

                    string stagedManifest = Assert.Single(
                        files,
                        path => path.EndsWith(moduleName + ".psd1", StringComparison.OrdinalIgnoreCase));
                    File.AppendAllText(
                        stagedManifest,
                        Environment.NewLine + "# SIG # simulated Authenticode mutation" + Environment.NewLine);
                }
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

    [Fact]
    public void Run_SignedUnifiedGitHubPackedArtifactLeavesReleaseProtectionOffByDefault()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, "PowerForge.ReleaseProvenance.json"), "{\"stale\":true}");
            File.WriteAllText(Path.Combine(root.FullName, "PowerForge.ReleaseProvenance.psd1"), "@{ Stale = $true }");
            var hostedOperations = new FakeHostedOperations { AutoSuccessfulSigningResult = true };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.UnifiedGitHubRelease = true;

            ModulePipelinePlan plan = runner.Plan(spec);
            ModulePipelineResult result = runner.Run(spec, plan);

            Assert.False(plan.RequireCleanReleaseSource);
            Assert.False(plan.RequireReleaseSourceUnchanged);
            Assert.False(plan.GenerateReleaseProvenance);
            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            Assert.Empty(artefact.EvidencePaths);
            using var archive = System.IO.Compression.ZipFile.OpenRead(artefact.OutputPath);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("PowerForge.ReleaseProvenance.psd1", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("PowerForge.ReleaseProvenance.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ConfigurationGateMode.Manifest, false)]
    [InlineData(ConfigurationGateMode.Documentation, false)]
    [InlineData(ConfigurationGateMode.Build, false)]
    [InlineData(ConfigurationGateMode.Publish, true)]
    public void Plan_ProvenanceActivationFollowsReleaseGate(
        ConfigurationGateMode gateMode,
        bool expectedActive)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration { Mode = gateMode }
                }
            }).ToArray();

            ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations()).Plan(spec);

            Assert.Equal(expectedActive, plan.GenerateReleaseProvenance);
            Assert.Equal(expectedActive, plan.RequireReleaseSourceUnchanged);
            Assert.Equal(expectedActive, plan.RequireCleanReleaseSource);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_ReleaseProtectionCanRequireCleanSourceWithoutProvenance()
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
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Segments = spec.Segments
                .Concat(new IConfigurationSegment[] { CreateReleaseProtection(requireCleanSource: true) })
                .ToArray();

            ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations()).Plan(spec);

            Assert.True(plan.RequireCleanReleaseSource);
            Assert.False(plan.RequireReleaseSourceUnchanged);
            Assert.False(plan.GenerateReleaseProvenance);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ReleaseProtectionCanRequireUnchangedSourceWithoutProvenance()
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
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Segments = spec.Segments
                .Concat(new IConfigurationSegment[] { CreateReleaseProtection(requireSourceUnchanged: true) })
                .ToArray();
            var runner = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true });
            ModulePipelinePlan plan = runner.Plan(spec);
            File.AppendAllText(Path.Combine(root.FullName, moduleName + ".psm1"), Environment.NewLine + "# changed after planning");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.True(plan.RequireCleanReleaseSource);
            Assert.True(plan.RequireReleaseSourceUnchanged);
            Assert.False(plan.GenerateReleaseProvenance);
            Assert.Contains("source changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SignedUnifiedGitHubPackedArtifactGeneratesReleaseEvidenceWithoutModulePublishSegment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.UnifiedGitHubRelease = true;
            EnableReleaseProvenance(spec);

            ModulePipelinePlan plan = runner.Plan(spec);
            ModulePipelineResult result = runner.Run(spec, plan);

            Assert.True(plan.GenerateReleaseProvenance);
            ArtefactBuildResult artefact = Assert.Single(result.ArtefactResults);
            string evidencePath = Assert.Single(artefact.EvidencePaths);
            Assert.True(File.Exists(evidencePath));
            Assert.Empty(hostedOperations.LastPublishedArtefacts);
            using var archive = System.IO.Compression.ZipFile.OpenRead(artefact.OutputPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.psd1");
            Assert.Contains(archive.Entries, entry => entry.FullName == "TestModule/PowerForge.ReleaseProvenance.json");
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

            Assert.Contains("clean release inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
            if (makeDirty)
                Assert.Contains("dirty.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_SignedGitHubPackedArtifactAllowsDirtyTrackedOperatorFileOutsideReleaseInputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            string moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module")).FullName;
            string buildRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Build")).FullName;
            string buildScript = Path.Combine(buildRoot, "Build-Project.ps1");
            WriteMinimalModule(moduleRoot, moduleName, "1.0.0");
            File.WriteAllText(buildScript, "param([string] $RunMode = 'Build')");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            File.WriteAllText(buildScript, "param([string] $RunMode = 'Publish')");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                moduleRoot,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);

            ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations()).Plan(spec);

            Assert.False(plan.SourceDirty);
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

            Assert.Contains("clean release inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_SignedGitHubPackedArtifactBindsEnabledIgnoredLifecycleScript(bool enabled)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Build/\nArtefacts/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string actionDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Build")).FullName;
            File.WriteAllText(Path.Combine(actionDirectory, "Invoke-ReleaseAction.ps1"), "# mutable ignored action");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Build.ExcludeDirectories = (spec.Build.ExcludeDirectories ?? Array.Empty<string>())
                .Concat(new[] { "Build" })
                .ToArray();
            EnableGitHubPublish(spec, moduleName);
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Enabled = enabled,
                        Name = "ignored release action",
                        At = ModulePipelineActionStage.BeforeArtefacts,
                        FilePath = Path.Combine("Build", "Invoke-ReleaseAction.ps1")
                    }
                }
            }).ToArray();

            if (enabled)
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    CreateRunner(new FakeHostedOperations()).Plan(spec));
                Assert.Contains("clean release inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                ModulePipelinePlan plan = CreateRunner(new FakeHostedOperations()).Plan(spec);
                Assert.True(plan.GenerateReleaseProvenance);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_SignedGitHubPackedArtifactBindsIgnoredArtefactCopyMapping(bool directoryMapping)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "generated/\nArtefacts/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string generatedDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "generated")).FullName;
            string generatedFile = Path.Combine(generatedDirectory, "payload.json");
            File.WriteAllText(generatedFile, "{\"mutable\":true}");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            ConfigurationArtefactSegment artefact = Assert.IsType<ConfigurationArtefactSegment>(
                spec.Segments.Single(segment => segment is ConfigurationArtefactSegment));
            var mapping = new ArtefactCopyMapping
            {
                Source = directoryMapping ? "generated" : Path.Combine("generated", "payload.json"),
                Destination = directoryMapping ? "generated" : Path.Combine("generated", "payload.json")
            };
            if (directoryMapping)
                artefact.Configuration.DirectoryOutput = new[] { mapping };
            else
                artefact.Configuration.FilesOutput = new[] { mapping };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CreateRunner(new FakeHostedOperations()).Plan(spec));

            Assert.Contains("clean release inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_SignedGitHubBinaryModuleRejectsIgnoredEvaluatedProjectInputOutsideModuleSource()
    {
        var repository = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            string moduleSource = Directory.CreateDirectory(Path.Combine(repository.FullName, "Module")).FullName;
            string projectDirectory = Directory.CreateDirectory(Path.Combine(repository.FullName, "src", "Binary")).FullName;
            string ignoredDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "ignored")).FullName;
            string projectPath = Path.Combine(projectDirectory, "Binary.csproj");
            WriteMinimalModule(moduleSource, moduleName, "1.0.0");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><AdditionalFiles Include="ignored/rules.json" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(repository.FullName, ".gitignore"), "src/Binary/ignored/\nArtifacts/\n");
            RunGit(repository.FullName, "init", "--quiet");
            RunGit(repository.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(repository.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(repository.FullName, "add", ".");
            RunGit(repository.FullName, "commit", "--quiet", "-m", "fixture");
            File.WriteAllText(Path.Combine(ignoredDirectory, "rules.json"), "{\"mutable\":true}");
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                moduleSource,
                moduleName,
                Path.Combine(repository.FullName, "Artifacts", "Packed"));
            spec.Build.CsprojPath = projectPath;
            spec.Build.Frameworks = new[] { "net10.0" };
            EnableGitHubPublish(spec, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CreateRunner(new FakeHostedOperations()).Plan(spec));

            Assert.Contains("clean release inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { repository.Delete(recursive: true); } catch { }
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
    public void Run_SignedGitHubPackedArtifactRejectsPostPlanFileAddedToMappedDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string mappedDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "ExternalAssets")).FullName;
            File.WriteAllText(Path.Combine(mappedDirectory, "approved.json"), "{\"approved\":true}");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "ExternalAssets/generated.json\nArtefacts/\n");
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
                        File.WriteAllText(Path.Combine(mappedDirectory, "generated.json"), "{\"generated\":true}");
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            ConfigurationArtefactSegment artefact = Assert.IsType<ConfigurationArtefactSegment>(
                spec.Segments.Single(segment => segment is ConfigurationArtefactSegment));
            artefact.Configuration.DirectoryOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = mappedDirectory,
                    Destination = "ExternalAssets"
                }
            ];
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
    public void Run_SignedGitHubModuleRejectsEmbeddedPackageSourceMutationBeforeRemotePublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string packageProject = Path.Combine(root.FullName, "Package.csproj");
            string packageSource = Path.Combine(root.FullName, "Package.cs");
            File.WriteAllText(packageProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(packageSource, "public static class PackageSource { }");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Artifacts/\nArtefacts/\nbin/\nobj/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations,
                packageBuildExecutor: (request, _, configPath) =>
                {
                    if (request.PublishNuget == true)
                    {
                        request.BuildSpecPrepared?.Invoke(new DotNetRepositoryReleaseSpec
                        {
                            RootPath = root.FullName,
                            Configuration = "Release",
                            Publish = true
                        });
                        File.AppendAllText(packageSource, Environment.NewLine + "// mutated during package build");
                        request.RemotePublishAttempted?.Invoke();
                    }
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationPackageBuildSegment
                {
                    Configuration = new PackageBuildConfiguration
                    {
                        Name = "Packages",
                        RootPath = root.FullName,
                        Build = true,
                        PublishNuget = true,
                        BuildBeforeModule = false
                    }
                }
            }).ToArray();
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Contains("package source changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UnchangedSourceProtectionDoesNotReusePackageBuildBeforePublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string packageProject = Path.Combine(root.FullName, "Package.csproj");
            string packageSource = Path.Combine(root.FullName, "Package.cs");
            string packageOutput = Path.Combine(root.FullName, "Packages");
            string packagePath = Path.Combine(packageOutput, "Package.1.0.0.nupkg");
            string feedPath = Path.Combine(root.FullName, "Feed");
            File.WriteAllText(packageProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(packageSource, "public static class PackageSource { }");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Packages/\nFeed/\nArtefacts/\nbin/\nobj/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            int executorCalls = 0;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                new FakeHostedOperations(),
                packageBuildExecutor: (request, _, configPath) =>
                {
                    executorCalls++;
                    if (request.PublishNuget == true)
                    {
                        request.BuildSpecPrepared?.Invoke(new DotNetRepositoryReleaseSpec
                        {
                            RootPath = root.FullName,
                            Configuration = "Release",
                            OutputPath = packageOutput,
                            Publish = true
                        });
                        request.RemotePublishAttempted?.Invoke();
                    }

                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");
                    if (executorCalls == 1)
                        File.AppendAllText(packageSource, Environment.NewLine + "// changed after package build");
                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    release.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Package",
                        CsprojPath = packageProject,
                        PackageId = "Package",
                        IsPackable = true,
                        Packages = new List<string> { packagePath }
                    });
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult { Success = true, Release = release }
                    };
                });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateReleaseProtection(requireSourceUnchanged: true),
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = root.FullName,
                            OutputPath = packageOutput,
                            Build = true,
                            PublishNuget = true,
                            PublishSource = feedPath,
                            PublishApiKey = "test-key"
                        }
                    }
                }
            };
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Equal(2, executorCalls);
            Assert.Contains("package source changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UnchangedSourceProtectionRechecksBeforeModulePublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string packageProject = Path.Combine(root.FullName, "Package.csproj");
            string packageSource = Path.Combine(root.FullName, "Package.cs");
            File.WriteAllText(packageProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(packageSource, "public static class PackageSource { }");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Packages/\nArtefacts/\nbin/\nobj/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            var hostedOperations = new FakeHostedOperations { AutoSuccessfulPublishResult = true };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations,
                packageBuildExecutor: (request, _, configPath) =>
                {
                    File.AppendAllText(packageSource, Environment.NewLine + "// changed after module build");
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult { Success = true }
                        }
                    };
                });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateReleaseProtection(requireSourceUnchanged: true),
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = root.FullName,
                            Build = true,
                            BuildBeforeModule = false
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            Enabled = true,
                            ApiKey = "test-key"
                        }
                    }
                }
            };
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Equal(0, hostedOperations.PublishCalls);
            Assert.Contains("source changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_UnchangedSourceProtectionRechecksAtEachRemoteModuleMutationBoundary(
        bool requiredModuleSideEffect)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            string sourcePath = Path.Combine(root.FullName, moduleName + ".psm1");
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Artefacts/\nbin/\nobj/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            Action mutateSource = () => File.AppendAllText(
                sourcePath,
                Environment.NewLine + "# changed at remote mutation boundary");
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true,
                InvokeRemoteSideEffectObserved = requiredModuleSideEffect,
                BeforeRemoteSideEffectObserved = requiredModuleSideEffect ? mutateSource : null,
                BeforeRemotePublishAttempted = requiredModuleSideEffect ? null : mutateSource
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                CreateReleaseProtection(requireSourceUnchanged: true),
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Destination = PublishDestination.PowerShellGallery,
                        Enabled = true,
                        ApiKey = "test-key"
                    }
                }
            }).ToArray();
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Contains("source changed after planning", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_SignedGitHubPackedArtifactRejectsManifestMutationAfterAuthorizedSync(bool mutateProjectManifest)
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
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true,
                ActionStarted = (_, context) =>
                {
                    string mutation = Environment.NewLine + "# changed after authorized manifest synchronization";
                    string manifestPath = mutateProjectManifest
                        ? Path.Combine(root.FullName, moduleName + ".psd1")
                        : context.ManifestPath!;
                    File.AppendAllText(manifestPath, mutation);
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Name = "mutate synchronized manifest",
                        At = ModulePipelineActionStage.BeforeArtefacts,
                        InlineScript = "# executed through the test host"
                    }
                }
            }).ToArray();
            ModulePipelinePlan plan = runner.Plan(spec);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));

            Assert.Contains("changed after its pipeline-owned synchronization", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Run_SignedGitHubMultiplePackedArtifactsExcludeEarlierReleaseOutputsFromSourceGuard()
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
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                AutoSuccessfulPublishResult = true
            };
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
            EnableGitHubPublish(spec, moduleName);

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            Assert.Equal(2, result.ArtefactResults.Length);
            Assert.All(result.ArtefactResults, artifact => Assert.Single(artifact.EvidencePaths));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void CollectModuleReleaseAssets_RetainsDeclaredMissingEvidenceForPublicationPreflight()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(root, "Sample.zip");
        string missingEvidence = Path.Combine(root, "Sample.zip.signing.json");
        var artefact = new ArtefactBuildResult(
            ArtefactType.Packed,
            "release",
            archive,
            Array.Empty<ArtefactModuleEntry>(),
            Array.Empty<ArtefactCopyEntry>(),
            new[] { missingEvidence });
        var method = typeof(ModulePipelineRunner).GetMethod(
            "CollectModuleReleaseAssets",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        string[] assets = Assert.IsType<string[]>(method!.Invoke(
            null,
            new object?[] { new[] { artefact }, "release" }));

        Assert.Equal(new[] { Path.GetFullPath(archive), Path.GetFullPath(missingEvidence) }, assets);
        Assert.False(File.Exists(missingEvidence));
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
            CreateReleaseProtection(generateProvenance: true),
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

    private static void EnableReleaseProvenance(ModulePipelineSpec spec)
    {
        spec.Segments = spec.Segments
            .Concat(new IConfigurationSegment[] { CreateReleaseProtection(generateProvenance: true) })
            .ToArray();
    }

    private static ConfigurationReleaseProtectionSegment CreateReleaseProtection(
        bool requireCleanSource = false,
        bool requireSourceUnchanged = false,
        bool generateProvenance = false)
        => new()
        {
            Configuration = new ReleaseProtectionConfiguration
            {
                RequireCleanSource = requireCleanSource,
                RequireSourceUnchanged = requireSourceUnchanged,
                GenerateProvenance = generateProvenance
            }
        };

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
