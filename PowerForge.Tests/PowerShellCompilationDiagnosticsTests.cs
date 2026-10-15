using System.Management.Automation.Language;
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
        Assert.Equal(13, manifest.SchemaVersion);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PowerShellCompilerGate")]
    public void HybridFailureMapDistinguishesFallbackAndCompiledLifecycleUnits(bool nested)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Fallback { trap { continue }; [int] $value = 1; Get-Variable -Name value -ValueOnly }; " +
            "function Invoke-Lifecycle { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int] $Value) " +
            "begin { $total = 0 } process { $total += $Value } end { $total } }; " +
            "Export-ModuleMember -Function Get-Fallback,Invoke-Lifecycle",
            ".psm1");
        if (nested)
        {
            var publicDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Public"));
            File.WriteAllText(Path.Combine(publicDirectory.FullName, "Functions.ps1"), File.ReadAllText(fixture.ScriptPath));
            File.WriteAllText(fixture.ScriptPath, ". \"$PSScriptRoot/Public/Functions.ps1\"");
        }
        var input = new PowerShellCompilationInputResolver().Resolve(new[] { fixture.ScriptPath },
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generic.HostedFailureMapProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = input.CompilationSourceFiles
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var entries = result.Manifest!.FailureMap!.Entries;
        Assert.Contains(entries, static entry =>
            entry.UnitName == "Get-Fallback" && entry.BoundaryContract == "PowerShellRuntime" && entry.GeneratedStartLine == 0);
        Assert.Contains(entries, static entry =>
            entry.UnitName == "Invoke-Lifecycle" && entry.BoundaryContract == "TypedClr+PowerShellRuntime" && entry.GeneratedStartLine > 0);
        if (nested)
            Assert.All(entries.Where(entry => entry.UnitName is "Get-Fallback" or "Invoke-Lifecycle"),
                entry => Assert.Equal("Public/Functions.ps1", entry.RelativePath));
    }

    [Theory]
    [InlineData("Get-Duplicate", false)]
    [InlineData("get-duplicate", false)]
    [InlineData("Get-Duplicate", true)]
    [InlineData("get-duplicate", true)]
    [Trait("Category", "PowerShellCompilerGate")]
    public void HybridFailureMapPreservesSameLineDuplicateDefinitionExtents(string secondName, bool conditionalDeclaration)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Duplicate { function Get-Duplicate { return 0 }; return 1 }; " +
            (conditionalDeclaration ? "if ($false) { function Get-Duplicate { return -1 } }; " : string.Empty) +
            "function " + secondName + " {\n    throw 'second'\n}", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generic.DuplicateFailureMapProof",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = result.Manifest!;
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(PowerShellCompilationMode.Hybrid,
            new[] { fixture.ScriptPath }, fixture.RootPath, manifest.TargetFramework,
            PowerShellCompilationCapabilities.HybridModule);
        var file = Assert.Single(plan.Files);
        var units = file.Units.Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function).ToArray();
        var declarations = PowerShellSourceParser.GetTopLevelFunctions(
            Parser.ParseFile(fixture.ScriptPath, out _, out _), PowerShellCompilationCapabilities.HybridModule).ToArray();
        Assert.Equal(conditionalDeclaration ? 3 : 2, declarations.Length);
        Assert.Equal(declarations.Length, units.Length);
        var relativePath = PowerShellCompilationExplanationService.NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath));
        var unitIds = units.Select(unit => PowerShellCompilationExplanationService.ComputeUnitId(relativePath, unit)).ToArray();
        Assert.Equal(units.Length, unitIds.Distinct(StringComparer.Ordinal).Count());

        for (var index = 0; index < units.Length; index++)
        {
            var declaration = declarations[index];
            var disposition = Assert.Single(manifest.UnitDispositionLedger!.Entries, entry => entry.UnitId == unitIds[index]);
            var entries = manifest.FailureMap!.Entries.Where(entry => entry.UnitId == unitIds[index]).ToArray();
            Assert.NotEmpty(entries);
            if (conditionalDeclaration && index == 1)
            {
                Assert.True(disposition.EmittedClrMethod);
                Assert.False(disposition.RetainedHostedSource);
                Assert.All(entries, entry =>
                {
                    Assert.Equal(disposition.GeneratedMemberName, entry.GeneratedMemberName);
                    Assert.True(entry.GeneratedStartLine > 0);
                    Assert.Equal("TypedClr+PowerShellRuntime", entry.BoundaryContract);
                    Assert.Equal(1, entry.SourceStartLine);
                    Assert.Equal(1, entry.SourceEndLine);
                    Assert.InRange(entry.SourceStartColumn, declaration.Body.Extent.StartColumnNumber, declaration.Body.Extent.EndColumnNumber);
                    Assert.InRange(entry.SourceEndColumn, entry.SourceStartColumn, declaration.Body.Extent.EndColumnNumber);
                });
                continue;
            }

            Assert.False(disposition.EmittedClrMethod);
            Assert.True(disposition.RetainedHostedSource);
            var fallback = Assert.Single(entries);
            Assert.Equal(0, fallback.GeneratedStartLine);
            Assert.Equal("PowerShellRuntime", fallback.BoundaryContract);
            Assert.Equal(declaration.Body.Extent.StartLineNumber, fallback.SourceStartLine);
            Assert.Equal(declaration.Body.Extent.StartColumnNumber, fallback.SourceStartColumn);
            Assert.Equal(declaration.Body.Extent.EndLineNumber, fallback.SourceEndLine);
            Assert.Equal(declaration.Body.Extent.EndColumnNumber, fallback.SourceEndColumn);
            Assert.Contains(disposition.DiagnosticChain, cause =>
                cause.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax &&
                cause.Line == declaration.Extent.StartLineNumber && cause.Column == declaration.Extent.StartColumnNumber);
        }

        var lastEntry = manifest.FailureMap!.Entries.First(entry => entry.UnitId == unitIds[unitIds.Length - 1]);
        var failure = PowerShellCompilationFailureMapper.MapRuntimeFailure(
            manifest, $"failure in {lastEntry.DocumentId}:line 2");
        Assert.Equal(unitIds[unitIds.Length - 1], Assert.Single(failure.Locations).UnitId);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void HybridFailureMapAssignsEachSameLineConditionalMethodToItsOwnUnit()
    {
        using var fixture = ArtifactFixture.Create(
            "if ($false) { function Get-Duplicate { return 1 } }; if ($true) { function Get-Duplicate { return 2 } }", ".psm1");
        var input = new PowerShellCompilationInputResolver().Resolve(new[] { fixture.ScriptPath },
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(PowerShellCompilationMode.Hybrid,
            input.CompilationSourceFiles, fixture.RootPath, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger, shaped.PromotedRegions);
        var declarations = PowerShellSourceParser.GetTopLevelFunctions(
            Parser.ParseFile(fixture.ScriptPath, out _, out _), PowerShellCompilationCapabilities.HybridModule).ToArray();
        Assert.Equal(declarations.Length, methods.Length);
        var manifest = new PowerShellCompilationArtifactManifest { FailureMap = map };
        Assert.All(methods, method =>
        {
            var declaration = Assert.Single(declarations,
                candidate => candidate.Extent.StartOffset == method.ConditionalDeclarationOffset);
            var unit = Assert.Single(ledger.Entries, candidate => candidate.GeneratedMemberName == method.GeneratedName);
            var segments = map.Entries.Where(candidate => candidate.GeneratedMemberName == method.GeneratedName).ToArray();
            Assert.Equal(method.SourceMap.Length, segments.Length);
            Assert.NotEmpty(segments);
            Assert.All(segments, segment =>
            {
                Assert.Equal(unit.UnitId, segment.UnitId);
                Assert.Equal("TypedClr+PowerShellRuntime", segment.BoundaryContract);
                Assert.True(segment.GeneratedStartLine > 0);
                Assert.InRange(segment.SourceStartColumn, declaration.Body.Extent.StartColumnNumber, declaration.Body.Extent.EndColumnNumber);
                Assert.InRange(segment.SourceEndColumn, segment.SourceStartColumn, declaration.Body.Extent.EndColumnNumber);
            });
            var mapped = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
                $"   at {shaped.NamespaceName}.{shaped.TypeName}.{method.GeneratedName}() in {method.DocumentId}:line {method.SourceLine}");
            Assert.Equal(unit.UnitId, Assert.Single(mapped.Locations).UnitId);
        });
        Assert.Equal(methods.Length, map.Entries.Where(static segment => segment.GeneratedStartLine > 0)
            .Select(static segment => segment.UnitId).Distinct(StringComparer.Ordinal).Count());
        var first = map.Entries.First(static segment => segment.GeneratedStartLine > 0);
        var last = map.Entries.Last(static segment => segment.GeneratedStartLine > 0);
        var insufficientIdentity = new[]
        {
            $"failure in {first.DocumentId}:line {first.SourceStartLine}",
            $"   at {shaped.NamespaceName}.{shaped.TypeName}.MissingGeneratedMember() in {first.DocumentId}:line {first.SourceStartLine}",
            $"   at {shaped.NamespaceName}.{shaped.TypeName}.{last.GeneratedMemberName}(){Environment.NewLine}" +
                $"failure in {first.DocumentId}:line {first.SourceStartLine}",
            $"{first.DocumentId}({first.SourceStartLine},{last.SourceStartColumn}): error CS0001: fixture diagnostic"
        };
        Assert.All(insufficientIdentity, failureText =>
            Assert.Empty(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, failureText).Locations));
    }

    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
        "[int] $value = 7; $value")]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
        "[int] $value = 7; $value")]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
        "[int]$script:Count = 1; function Get-Value { param() return $script:Count }")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapRetainsEmittedScriptAndModuleInitializerOwners(
        PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode, string source)
    {
        using var fixture = ArtifactFixture.Create(source, kind == PowerShellCompilationArtifactKind.Executable ? ".ps1" : ".psm1");
        var input = new PowerShellCompilationInputResolver().Resolve(new[] { fixture.ScriptPath }, kind, mode);
        var target = kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Hybrid
            ? PowerShellCompilationTargetContractService.Create(kind, mode, "net10.0", "win-x64", false, false,
                PowerShellCompilationExecutableOptimization.None, true)
            : null;
        var analyzer = new PowerShellCompilationAnalyzer();
        var plan = target is null
            ? analyzer.AnalyzeFiles(mode, input.CompilationSourceFiles, fixture.RootPath, "net10.0",
                PowerShellCompilationBuildSpec.GetCapabilities(kind, mode))
            : analyzer.Analyze(input, mode, "net10.0", PowerShellCompilationResourceMode.Declared,
                null, null, fixture.OutputPath, target);
        Assert.True(plan.CanProceed);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry);
        Assert.NotNull(shaped);
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        if (mode == PowerShellCompilationMode.Hybrid)
        {
            Assert.NotNull(entry);
            Assert.NotNull(entry.EntryPointMethod.NativeFunctionBinding);
        }
        if (kind == PowerShellCompilationArtifactKind.Library)
        {
            Assert.NotNull(shaped.RuntimeFreeModule);
            Assert.Single(methods, static method => method.IsModuleInitializer);
        }
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger, shaped.PromotedRegions);
        Assert.Contains(methods, static method => method.SourceName == "<script>");
        Assert.All(methods, method =>
        {
            var owner = Assert.Single(ledger.Entries, unit => unit.GeneratedMemberName == method.GeneratedName);
            var segments = map.Entries.Where(segment => segment.GeneratedMemberName == method.GeneratedName && segment.GeneratedStartLine > 0).ToArray();
            Assert.NotEmpty(segments);
            Assert.All(segments, segment => Assert.Equal(owner.UnitId, segment.UnitId));
        });
    }

    [Theory]
    [InlineData("empty", 0, true)]
    [InlineData("comment-only", 0, true)]
    [InlineData("declaration-only", 1, true)]
    [InlineData("parameter-only", 1, false)]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapPreservesEmptyAndParameterOwnedExecutableRoots(
        string caseName, int authoredUnits, bool syntheticNoOp)
    {
        var source = caseName switch
        {
            "empty" => string.Empty,
            "comment-only" => "  \n# Empty entrypoint\n",
            "declaration-only" => "# Empty entrypoint\nfunction Get-Answer { return 42 }",
            "parameter-only" => "param([int] $Count = 4)",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(new[] { fixture.ScriptPath },
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict);
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(input.Mode, input.CompilationSourceFiles,
            fixture.RootPath, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var root = Assert.Single(methods, method => method.SourceName == "<script>");
        Assert.Equal(syntheticNoOp, root.IsSyntheticNoOpExecutableEntry);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger);
        Assert.Equal(authoredUnits, plan.TotalUnits);
        Assert.Equal(authoredUnits, ledger.AnalyzedUnits);
        Assert.Equal(authoredUnits, ledger.EmittedUnits);
        Assert.Equal(syntheticNoOp ? 0 : 1, ledger.Entries.Count(unit => unit.Kind == PowerShellCompilationUnitKind.Script));
        Assert.All(map.Entries, segment => Assert.Contains(ledger.Entries, unit => unit.UnitId == segment.UnitId));
        Assert.All(methods.Where(method => !method.IsSyntheticNoOpExecutableEntry),
            method => Assert.Contains(map.Entries, segment => segment.GeneratedMemberName == method.GeneratedName));
        if (syntheticNoOp)
        {
            Assert.DoesNotContain(map.Entries, segment => segment.GeneratedMemberName == root.GeneratedName);
        }
        else
        {
            var unboundCopy = new PowerShellCompiledMethod(root.SourceName, root.GeneratedName,
                root.ReturnType, root.Parameters, root.SourceLine, root.SourcePath);
            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, new[] { unboundCopy }, ledger));
            Assert.Contains("has no final unit-disposition owner", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapBindsSameNamedExecutableAndFunctionMethodsToDistinctFinalOwners()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int] $Count = 4)\nfunction Invoke { param([int] $Value) return $Value * 2 }\nInvoke $Count");
        var input = new PowerShellCompilationInputResolver().Resolve(new[] { fixture.ScriptPath },
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(input.Kind, input.Mode,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, input.Mode, "net10.0",
            PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        Assert.NotNull(entry);
        Assert.NotNull(entry.EntryPointMethod.NativeFunctionBinding);
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var function = Assert.Single(methods, method => method.SourceName == "Invoke");
        Assert.NotSame(function, entry.EntryPoint.Method);
        Assert.Equal(function.GeneratedName, entry.EntryPoint.Method.GeneratedName);
        Assert.Equal(function.SourcePath, entry.EntryPoint.Method.SourcePath);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger);
        var rootOwner = Assert.Single(ledger.Entries, unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        var functionOwner = Assert.Single(ledger.Entries, unit => unit.Kind == PowerShellCompilationUnitKind.Function);
        Assert.NotEqual(rootOwner.UnitId, functionOwner.UnitId);
        Assert.NotEqual(entry.EntryPoint.Method.GeneratedDeclaringTypeName, function.GeneratedDeclaringTypeName);
        foreach (var pair in new[] { (Method: entry.EntryPoint.Method, Owner: rootOwner), (Method: function, Owner: functionOwner) })
        {
            var nestedMaps = pair.Method.NestedBodies.SelectMany(static body => body.SourceMap).ToHashSet();
            var segments = map.Entries.Where(segment => segment.UnitId == pair.Owner.UnitId &&
                segment.GeneratedDeclaringTypeName == pair.Method.GeneratedDeclaringTypeName &&
                segment.GeneratedMemberName == pair.Method.GeneratedName).ToArray();
            Assert.Equal(pair.Method.SourceMap.Count(segment => !nestedMaps.Contains(segment)), segments.Length);
            Assert.NotEmpty(segments);
            Assert.All(segments, segment => Assert.Equal(pair.Method.GeneratedName, segment.GeneratedMemberName));
        }
        var privateBody = Assert.Single(entry.EntryPoint.Method.NestedBodies);
        Assert.Equal(entry.EntryPoint.Method.GeneratedDeclaringTypeName, privateBody.GeneratedDeclaringTypeName);
        Assert.Same(functionOwner, ledger.GetEmittedBodyOwner(privateBody));
        var privateSegments = map.Entries.Where(segment => segment.GeneratedDeclaringTypeName == privateBody.GeneratedDeclaringTypeName &&
            segment.GeneratedMemberName == privateBody.GeneratedMemberName).ToArray();
        Assert.Equal(privateBody.SourceMap.Length, privateSegments.Length);
        Assert.NotEmpty(privateSegments);
        Assert.All(privateSegments, segment => Assert.Equal(functionOwner.UnitId, segment.UnitId));
        Assert.Equal(entry.EntryPoint.Method.SourceMap.Length,
            privateSegments.Length + map.Entries.Count(segment => segment.UnitId == rootOwner.UnitId));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilation")]
    public void DiagnosticsRedactPathsAndSecretAssignments()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "PowerForge.Redaction", Guid.NewGuid().ToString("N"), "input.ps1");
        var plan = new PowerShellCompilationPlan(
            PowerShellCompilationMode.Strict,
            new[] { new PowerShellCompilationFilePlan(sourcePath, "input.ps1", Array.Empty<PowerShellCompilationUnitPlan>(), Array.Empty<PowerShellCompilationDiagnostic>()) },
            "net10.0");

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
