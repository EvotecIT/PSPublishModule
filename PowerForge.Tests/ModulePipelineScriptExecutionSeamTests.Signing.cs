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
            Assert.Equal(2, hostedOperations.SignCalls);
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
            Assert.Equal(2, hostedOperations.SignCalls);
            Assert.Equal(2, hostedOperations.SigningRootPaths.Count);
            Assert.NotEqual(hostedOperations.SigningRootPaths[0], hostedOperations.SigningRootPaths[1]);
            Assert.DoesNotContain("Modules", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(artefact.OutputPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
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
