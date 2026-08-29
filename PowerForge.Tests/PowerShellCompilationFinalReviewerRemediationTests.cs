using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void UnitDispositionLedgerAttributesRuntimeDependenciesOnlyToTheirOwningSource()
    {
        using var fixture = ArtifactFixture.Create("return 1");
        var sibling = Path.Combine(fixture.RootPath, "Sibling.ps1");
        File.WriteAllText(sibling, "return 2");
        var firstUnit = new PowerShellCompilationUnitPlan(
            "<script>", PowerShellCompilationUnitKind.Script, 1, typeof(object).FullName!,
            Array.Empty<PowerShellCompilationParameter>(), Array.Empty<PowerShellCompilationDiagnostic>());
        var secondUnit = new PowerShellCompilationUnitPlan(
            "<script>", PowerShellCompilationUnitKind.Script, 1, typeof(object).FullName!,
            Array.Empty<PowerShellCompilationParameter>(), Array.Empty<PowerShellCompilationDiagnostic>());
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Package,
            new[]
            {
                new PowerShellCompilationFilePlan(fixture.ScriptPath, "Tool.ps1", new[] { firstUnit }, Array.Empty<PowerShellCompilationDiagnostic>()),
                new PowerShellCompilationFilePlan(sibling, "Sibling.ps1", new[] { secondUnit }, Array.Empty<PowerShellCompilationDiagnostic>())
            },
            dependencies: new[]
            {
                RuntimeSource(fixture.ScriptPath, "Tool.ps1", "first-source"),
                RuntimeSource(sibling, "Sibling.ps1", "second-source"),
                new PowerShellCompilationDependency(
                    "External.Runtime", null, "External.Runtime",
                    PowerShellCompilationDependencyKind.RequiredModule,
                    PowerShellCompilationDependencyDiscovery.RequiredModules,
                    PowerShellCompilationDependencyDisposition.ExternalRequirement,
                    exists: false, sizeBytes: 0, "module-runtime")
            });

        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan,
            PowerShellCompilationArtifactKind.Executable,
            shapedCompilation: null,
            fixture.ScriptPath);

        var first = Assert.Single(ledger.Entries, static entry => entry.RelativePath == "Tool.ps1");
        var second = Assert.Single(ledger.Entries, static entry => entry.RelativePath == "Sibling.ps1");
        Assert.Contains(first.DependencyCauses, static cause => cause.Contains("first-source", StringComparison.Ordinal));
        Assert.DoesNotContain(first.DependencyCauses, static cause => cause.Contains("second-source", StringComparison.Ordinal));
        Assert.Contains(second.DependencyCauses, static cause => cause.Contains("second-source", StringComparison.Ordinal));
        Assert.DoesNotContain(second.DependencyCauses, static cause => cause.Contains("first-source", StringComparison.Ordinal));
        Assert.DoesNotContain(ledger.Entries.SelectMany(static entry => entry.DependencyCauses), static cause =>
            cause.Contains("External.Runtime", StringComparison.Ordinal));
        Assert.Contains(ledger.DeliveryRuntimeCauses, static cause => cause.Contains("External.Runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void UnitDispositionLedgerPreservesCaseSensitiveSourceIdentity()
    {
        using var fixture = ArtifactFixture.Create("return 1");
        if (PowerShellCompilationPathSafety.GetPathComparison(fixture.RootPath) != StringComparison.Ordinal)
            return;
        var upper = Path.Combine(fixture.RootPath, "A.ps1");
        var lower = Path.Combine(fixture.RootPath, "a.ps1");
        File.WriteAllText(upper, "return 1");
        File.WriteAllText(lower, "return 2");
        var unit = new PowerShellCompilationUnitPlan(
            "<script>", PowerShellCompilationUnitKind.Script, 1, typeof(object).FullName!,
            Array.Empty<PowerShellCompilationParameter>(), Array.Empty<PowerShellCompilationDiagnostic>());
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Package,
            new[]
            {
                new PowerShellCompilationFilePlan(upper, "A.ps1", new[] { unit }, Array.Empty<PowerShellCompilationDiagnostic>()),
                new PowerShellCompilationFilePlan(lower, "a.ps1", new[] { unit }, Array.Empty<PowerShellCompilationDiagnostic>())
            },
            dependencies: new[]
            {
                RuntimeSource(upper, "A.ps1", "upper-source"),
                RuntimeSource(lower, "a.ps1", "lower-source")
            });

        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan,
            PowerShellCompilationArtifactKind.Executable,
            shapedCompilation: null,
            upper);

        var upperEntry = Assert.Single(ledger.Entries, static entry => entry.RelativePath == "A.ps1");
        var lowerEntry = Assert.Single(ledger.Entries, static entry => entry.RelativePath == "a.ps1");
        Assert.Single(upperEntry.DependencyCauses);
        Assert.Contains("upper-source", upperEntry.DependencyCauses[0], StringComparison.Ordinal);
        Assert.Single(lowerEntry.DependencyCauses);
        Assert.Contains("lower-source", lowerEntry.DependencyCauses[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ReproductionEvidenceRejectsDiagnosticFileIdentityTampering()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HostedValue { Get-Date }; Export-ModuleMember -Function Get-HostedValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DiagnosticIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        var diagnostic = Assert.Single(manifest.Diagnostics);
        Assert.False(Path.IsPathRooted(diagnostic.FilePath));
        manifest.Diagnostics = new[]
        {
            new PowerShellCompilationDiagnostic(
                diagnostic.Code,
                diagnostic.Message,
                "tampered/" + diagnostic.FilePath,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.FeatureId)
        };

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
    }

    [Fact]
    public void DeliveredSigningEvidenceDropsAutoRevisionManifestButRetainsByteIdenticalPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Delivered Signing Tests", Guid.NewGuid().ToString("N"));
        var signedRoot = Path.Combine(root, "signed");
        var installPackage = Path.Combine(root, "package");
        var installRoot = Path.Combine(root, "modules");
        const string moduleName = "GenericCompiledModule";
        try
        {
            Directory.CreateDirectory(signedRoot);
            Directory.CreateDirectory(installPackage);
            Directory.CreateDirectory(Path.Combine(installRoot, moduleName, "1.0.0"));
            var signedManifest = Path.Combine(signedRoot, moduleName + ".psd1");
            var signedAssembly = Path.Combine(signedRoot, moduleName + ".dll");
            File.WriteAllText(signedManifest, "@{ RootModule = 'GenericCompiledModule.dll'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(signedAssembly, "binary-payload");
            File.Copy(signedManifest, Path.Combine(installPackage, moduleName + ".psd1"));
            File.Copy(signedAssembly, Path.Combine(installPackage, moduleName + ".dll"));
            var signed = new ModuleSigningResult
            {
                AlreadySignedByThisCert = 2,
                CertificateThumbprint = "AABBCC",
                VerifiedFilePaths = new[] { signedManifest, signedAssembly }
            };
            var installed = ModuleBuildPipelineFactory.Create(new NullLogger()).InstallFromStaging(new ModuleInstallSpec
            {
                Name = moduleName,
                Version = "1.0.0",
                StagingPath = installPackage,
                Strategy = InstallationStrategy.AutoRevision,
                KeepVersions = 5,
                Roots = new[] { installRoot },
                UpdateManifestToResolvedVersion = true
            });

            Assert.Equal("1.0.0.1", installed.Version);
            var installedRoot = Assert.Single(installed.InstalledPaths);
            var delivered = Assert.IsType<ModuleSigningResult>(ModulePipelineRunner.CreateDeliveredSigningResult(
                signed,
                signedRoot,
                installedRoot));
            var verified = Assert.Single(delivered.VerifiedFilePaths);
            Assert.EndsWith(moduleName + ".dll", verified, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(delivered.VerifiedFilePaths, static path =>
                path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static PowerShellCompilationDependency RuntimeSource(string sourcePath, string relativePath, string note)
        => new(
            Path.GetFileName(sourcePath),
            sourcePath,
            relativePath,
            PowerShellCompilationDependencyKind.PowerShellSource,
            PowerShellCompilationDependencyDiscovery.SourceGraph,
            PowerShellCompilationDependencyDisposition.PreservedScript,
            exists: true,
            sizeBytes: new FileInfo(sourcePath).Length,
            note);
}
