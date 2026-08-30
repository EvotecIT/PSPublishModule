using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void BuildPublishesOptionalPortableDiagnosticsAndFailsClosedOnAbiDrift()
    {
        const string authoredMarker = "private-authored-marker";
        using var fixture = ArtifactFixture.Create($$"""
            function Get-Value {
                param([int] $Value)
                $unused = '{{authoredMarker}}'
                return $Value
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generic.DiagnosticsProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true);

        var baseline = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(baseline.Succeeded, baseline.Error + Environment.NewLine + baseline.BuildOutput);
        var baselineManifest = Assert.IsType<PowerShellCompilationArtifactManifest>(baseline.Manifest);
        Assert.False(Assert.IsType<PowerShellCompilationIrSnapshotEvidence>(baselineManifest.IrSnapshots).Emitted);
        Assert.Equal(string.Empty, baselineManifest.Reproduction!.IrSnapshotsSha256);
        var expectedAbi = Assert.IsType<PowerShellCompilationAbiManifest>(baselineManifest.PublicAbi).Sha256;

        spec.EmitIrSnapshots = true;
        spec.ExpectedPublicAbiSha256 = expectedAbi;
        var diagnosed = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(diagnosed.Succeeded, diagnosed.Error + Environment.NewLine + diagnosed.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(diagnosed.Manifest);
        Assert.Equal(12, manifest.SchemaVersion);
        var snapshots = Assert.IsType<PowerShellCompilationIrSnapshotEvidence>(manifest.IrSnapshots);
        Assert.True(snapshots.Emitted);
        Assert.Equal(64, snapshots.Sha256.Length);
        var snapshotFile = Assert.Single(manifest.Files, static file => file.Role == "CompilerIrSnapshot");
        var snapshotText = File.ReadAllText(snapshotFile.Path);
        Assert.DoesNotContain(fixture.RootPath, snapshotText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(authoredMarker, snapshotText, StringComparison.Ordinal);
        Assert.DoesNotContain("ScriptBlockAst", snapshotText, StringComparison.Ordinal);
        Assert.NotEmpty(manifest.FailureMap!.Entries);
        Assert.Contains(manifest.DiagnosticAudit!.Events, static item => item.Category == "Cache");
        Assert.Contains(manifest.DiagnosticAudit.Events, static item => item.Category == "DependencyGraph");
        Assert.Contains(manifest.DiagnosticAudit.Events, static item => item.Category == "Abi" && item.Outcome == "Matched");
        Assert.Contains(manifest.DiagnosticAudit.Events, static item => item.Category == "FallbackCrossings");
        Assert.True(manifest.DiagnosticsPolicy!.LocalOnly);
        Assert.False(manifest.DiagnosticsPolicy.AutomaticUpload);
        Assert.Equal(snapshots.Sha256, manifest.Reproduction!.IrSnapshotsSha256);
        Assert.Equal(manifest.FailureMap.Sha256, manifest.Reproduction.FailureMapSha256);
        Assert.Equal(manifest.DiagnosticAudit.Sha256, manifest.Reproduction.DiagnosticAuditSha256);
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);

        var mapEntry = manifest.FailureMap.Entries[0];
        var mapped = PowerShellCompilationFailureMapper.MapRuntimeFailure(
            manifest,
            $"failure in {mapEntry.DocumentId}:line {mapEntry.SourceStartLine} token=do-not-retain");
        var location = Assert.Single(mapped.Locations);
        Assert.Equal(mapEntry.RelativePath, location.RelativePath);
        Assert.Equal(mapEntry.UnitId, location.UnitId);
        Assert.DoesNotContain(fixture.RootPath, JsonSerializer.Serialize(mapped), StringComparison.OrdinalIgnoreCase);

        var originalAuditHash = manifest.DiagnosticAudit.Sha256;
        manifest.DiagnosticAudit.Sha256 = new string('0', 64);
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
        manifest.DiagnosticAudit.Sha256 = originalAuditHash;

        var originalAuditReason = manifest.DiagnosticAudit.Events[0].Reason;
        manifest.DiagnosticAudit.Events[0].Reason = "tampered-reason";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
        manifest.DiagnosticAudit.Events[0].Reason = originalAuditReason;

        var originalBoundary = manifest.FailureMap.Entries[0].BoundaryContract;
        manifest.FailureMap.Entries[0].BoundaryContract = "TamperedBoundary";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
        manifest.FailureMap.Entries[0].BoundaryContract = originalBoundary;
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);

        spec.ExpectedPublicAbiSha256 = new string('0', 64);
        var drift = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.False(drift.Succeeded);
        Assert.Equal(PowerShellCompilationFailureStage.Abi, drift.Failure!.Stage);
        Assert.Equal("AbiFailure", drift.Failure.Reason);
        Assert.DoesNotContain(fixture.RootPath, drift.Failure.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void StrictBinaryModulePublishesAndEnforcesItsPublicAbi()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { [CmdletBinding()] param([int] $Value) return $Value }",
            ".psm1");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generic.BinaryAbiProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true);

        var baseline = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(baseline.Succeeded, baseline.Error + Environment.NewLine + baseline.BuildOutput);
        var expectedAbi = Assert.IsType<PowerShellCompilationAbiManifest>(baseline.Manifest!.PublicAbi).Sha256;
        Assert.Equal(64, expectedAbi.Length);

        spec.ExpectedPublicAbiSha256 = expectedAbi;
        var matched = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(matched.Succeeded, matched.Error + Environment.NewLine + matched.BuildOutput);
        Assert.Equal(expectedAbi, matched.Manifest!.PublicAbi!.Sha256);
        Assert.Contains(matched.Manifest.DiagnosticAudit!.Events, static item =>
            item.Category == "Abi" && item.Outcome == "Matched");
        var embeddedAbi = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                System.Reflection.Assembly.LoadFrom(matched.ArtifactPath!))
            .Single(static attribute => attribute.Key == "PowerForge.PublicAbiSha256");
        Assert.Equal(expectedAbi, embeddedAbi.Value);

        spec.ExpectedPublicAbiSha256 = new string('0', 64);
        var drift = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(drift.Succeeded);
        Assert.Equal(PowerShellCompilationFailureStage.Abi, drift.Failure!.Stage);
        Assert.Equal("AbiFailure", drift.Failure.Reason);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void TypedCompilationResultRetainsThePublicNineArgumentConstructor()
    {
        var constructor = typeof(PowerShellTypedCompilationResult).GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(PowerShellCompiledMethod[]),
            typeof(PowerShellCompilationDiagnostic[]),
            typeof(string[]),
            typeof(PowerShellCompilationLifecycleSource[]),
            typeof(PowerShellCompilationOptimizationEvidence)
        });

        Assert.NotNull(constructor);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void HybridFailureMapIncludesPureFallbackAndHostedLifecycleUnits()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Fallback { [int] $value = 1; Get-Variable -Name value -ValueOnly }; " +
            "function Invoke-Lifecycle { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) " +
            "begin { $total = 0 } process { $total += $Value } end { $total } }; " +
            "Export-ModuleMember -Function Get-Fallback,Invoke-Lifecycle",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generic.HostedFailureMapProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var entries = result.Manifest!.FailureMap!.Entries;
        Assert.Contains(entries, static entry =>
            entry.UnitName == "Get-Fallback" && entry.BoundaryContract == "PowerShellRuntime" && entry.GeneratedStartLine == 0);
        Assert.Contains(entries, static entry =>
            entry.UnitName == "Invoke-Lifecycle" && entry.BoundaryContract == "PowerShellRuntime" && entry.GeneratedStartLine == 0);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void DiagnosticsRedactPathsAndSecretAssignments()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "PowerForge.Redaction", Guid.NewGuid().ToString("N"), "input.ps1");
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Strict,
            new[] { new PowerShellCompilationFilePlan(sourcePath, "input.ps1", Array.Empty<PowerShellCompilationUnitPlan>(), Array.Empty<PowerShellCompilationDiagnostic>()) },
            "net8.0");

        var redacted = PowerShellCompilationDiagnosticsEvidenceBuilder.Redact(
            plan,
            $"{sourcePath} password=visible token:another api_key='third'");

        Assert.Contains("input.ps1", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(sourcePath)!, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("visible", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("another", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("third", redacted, StringComparison.Ordinal);
        Assert.Equal(3, redacted.Split("<redacted-secret>", StringSplitOptions.None).Length - 1);
    }
}
