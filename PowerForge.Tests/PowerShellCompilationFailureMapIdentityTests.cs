using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("same-line")]
    [InlineData("multiline")]
    [InlineData("disjoint-name")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapResolvesQualifiedExecutableAndFunctionFrames(string caseName)
    {
        var functionName = caseName == "disjoint-name" ? "Invoke-Owned" : "Invoke";
        var source = caseName == "multiline"
            ? "param([int] $Count = 4)\nfunction Invoke { param([int] $Value) return $Value * 2 }\nInvoke $Count"
            : $"param([int] $Count = 4); function {functionName} {{ param([int] $Value) return $Value * 2 }}; {functionName} $Count";
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(input.Kind, input.Mode,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, input.Mode, "net10.0",
            PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        Assert.True(plan.CanProceed);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        Assert.NotNull(entry);
        Assert.NotNull(entry.EntryPointMethod.NativeFunctionBinding);
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var root = entry.EntryPoint.Method;
        var function = Assert.Single(shaped.Methods, method => method.SourceName == functionName);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger);
        var manifest = new PowerShellCompilationArtifactManifest { FailureMap = map };
        var rootOwner = Assert.Single(ledger.Entries, unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        var functionOwner = Assert.Single(ledger.Entries, unit => unit.Name == functionName);
        var rootLine = caseName == "multiline" ? 3 : 1;
        var functionLine = caseName == "multiline" ? 2 : 1;
        Assert.Equal("PowerForge.Compiled.CompiledNativeScriptEntry", root.GeneratedDeclaringTypeName);
        Assert.Equal(shaped.NamespaceName + "." + shaped.TypeName, function.GeneratedDeclaringTypeName);
        Assert.All(map.Entries.Where(segment => segment.UnitId == rootOwner.UnitId),
            segment => Assert.Equal(root.GeneratedDeclaringTypeName, segment.GeneratedDeclaringTypeName));
        Assert.All(map.Entries.Where(segment => segment.UnitId == functionOwner.UnitId && segment.GeneratedMemberName == function.GeneratedName),
            segment => Assert.Equal(function.GeneratedDeclaringTypeName, segment.GeneratedDeclaringTypeName));
        var nested = Assert.Single(root.NestedBodies, body => body.DeclarationName == functionName);
        Assert.Same(functionOwner, ledger.GetEmittedBodyOwner(nested));
        Assert.All(map.Entries.Where(segment => segment.GeneratedMemberName == nested.GeneratedMemberName), segment =>
        {
            Assert.Equal(functionOwner.UnitId, segment.UnitId);
            Assert.Equal(root.GeneratedDeclaringTypeName, segment.GeneratedDeclaringTypeName);
        });
        // These selector inputs are synthetic frames over the actual shaped map.
        // The separate artifact test below requires real exception output.
        Assert.Equal(rootOwner.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            $"at {root.GeneratedDeclaringTypeName}.{root.GeneratedName}() in {root.DocumentId}:line {rootLine}").Locations).UnitId);
        Assert.Equal(functionOwner.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            $"at {function.GeneratedDeclaringTypeName}.{function.GeneratedName}() in {function.DocumentId}:line {functionLine}").Locations).UnitId);
        Assert.Empty(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            $"at Other.DeclaringType.{root.GeneratedName}() in {root.DocumentId}:line {rootLine}").Locations);
        var bare = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            $"at {root.GeneratedName}() in {root.DocumentId}:line {rootLine}");
        if (caseName == "same-line") Assert.Empty(bare.Locations);
        else Assert.Equal(rootOwner.UnitId, Assert.Single(bare.Locations).UnitId);
        var documentOnly = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            $"in {root.DocumentId}:line {rootLine}");
        if (caseName == "multiline") Assert.Equal(rootOwner.UnitId, Assert.Single(documentOnly.Locations).UnitId);
        else Assert.Empty(documentOnly.Locations);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapPreservesSourcePathCasePolicyAndEachRetainedOwner()
    {
        using var fixture = ArtifactFixture.Create("return 0");
        var caseSensitive = PowerShellCompilationPathSafety.GetPathComparison(fixture.RootPath) == StringComparison.Ordinal;
        var upper = Path.Combine(fixture.RootPath, "A.ps1");
        var lower = Path.Combine(fixture.RootPath, "a.ps1");
        File.WriteAllText(upper, "return 1");
        if (caseSensitive) File.WriteAllText(lower, "return 2");
        var paths = caseSensitive ? new[] { upper, lower } : new[] { upper };
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(PowerShellCompilationMode.Package,
            paths, fixture.RootPath, null, PowerShellCompilationCapability.None);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan,
            PowerShellCompilationArtifactKind.Executable, null, upper);
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan,
            Array.Empty<PowerShellCompiledMethod>(), ledger);
        Assert.Equal(paths.Length, ledger.Entries.Count);
        Assert.Equal(paths.Length, map.Entries.Length);
        Assert.All(map.Entries, entry => Assert.Equal(caseSensitive, entry.SourcePathCaseSensitive));
        var manifest = new PowerShellCompilationArtifactManifest { FailureMap = map };
        foreach (var owner in ledger.Entries)
        {
            var mapped = Assert.Single(map.Entries, entry => entry.UnitId == owner.UnitId);
            Assert.True(owner.RetainedHostedSource);
            Assert.Equal(owner.RelativePath, mapped.RelativePath);
            foreach (var path in new[] { owner.RelativePath, mapped.DocumentId, fixture.RootPath.Replace('\\', '/') + "/" + owner.RelativePath })
                Assert.Equal(owner.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
                    $"in {path}:line 1").Locations).UnitId);
        }
        var lowercase = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, "in a.ps1:line 1");
        Assert.Equal(Assert.Single(ledger.Entries, owner => owner.RelativePath == (caseSensitive ? "a.ps1" : "A.ps1")).UnitId,
            Assert.Single(lowercase.Locations).UnitId);
        if (caseSensitive)
        {
            Assert.NotEqual(File.ReadAllText(upper), File.ReadAllText(lower));
            Assert.Empty(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, "in a.PS1:line 1").Locations);
            // Older maps lack the origin policy. Exact identities remain useful, while
            // a folded path shared by two real files must remain ambiguous.
            foreach (var entry in map.Entries) entry.SourcePathCaseSensitive = null;
            Assert.Empty(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, "in a.PS1:line 1").Locations);
        }
        else
        {
            var owner = Assert.Single(ledger.Entries);
            foreach (var path in new[] { "a.ps1", "A.PS1", "relocated/a.ps1" })
                Assert.Equal(owner.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
                    $"in {path}:line 1").Locations).UnitId);
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapPreservesLegacyDigestAndRoundTripsOptionalIdentity()
    {
        // Unchanged24 canonical Package map captured before the additive identity fields.
        const string legacyDigest = "919f2ec4d93e7fda30ca3cb36d03af49f87714fcc446ddcc1aea06ff438d72d6";
        const string legacyJson = """
            {"schemaVersion":1,"entries":[{"documentId":"0912f805aaa64ec9c261d4dd99934ad5beae54e9e978414e46943ffe6d9fadba","relativePath":"A.ps1","unitId":"22f315d5bed2b2e9ce61efb1","unitName":"<script>","generatedMemberName":"","sourceStartLine":1,"sourceStartColumn":1,"sourceEndLine":1,"sourceEndColumn":9,"generatedStartLine":0,"generatedEndLine":0,"boundaryContract":"PowerShellRuntime"}],"sha256":"919f2ec4d93e7fda30ca3cb36d03af49f87714fcc446ddcc1aea06ff438d72d6"}
            """;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var map = JsonSerializer.Deserialize<PowerShellCompilationFailureMap>(legacyJson, options)!;
        var entry = Assert.Single(map.Entries);
        Assert.Null(entry.GeneratedDeclaringTypeName);
        Assert.Null(entry.SourcePathCaseSensitive);
        Assert.Equal(legacyDigest, PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeFailureMapSha256(map));
        var roundTrip = JsonSerializer.Deserialize<PowerShellCompilationFailureMap>(JsonSerializer.Serialize(map, options), options)!;
        Assert.Equal(legacyDigest, PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeFailureMapSha256(roundTrip));
        Assert.Equal(entry.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(
            new PowerShellCompilationArtifactManifest { FailureMap = map }, "in a.ps1:line 1").Locations).UnitId);
        entry.GeneratedDeclaringTypeName = "PowerForge.Compiled.OwnedMethods";
        entry.SourcePathCaseSensitive = true;
        var identityDigest = PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeFailureMapSha256(map);
        Assert.NotEqual(legacyDigest, identityDigest);
        roundTrip = JsonSerializer.Deserialize<PowerShellCompilationFailureMap>(JsonSerializer.Serialize(map, options), options)!;
        Assert.Equal(identityDigest, PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeFailureMapSha256(roundTrip));
        Assert.Equal(entry.GeneratedDeclaringTypeName, roundTrip.Entries[0].GeneratedDeclaringTypeName);
        Assert.Equal(entry.SourcePathCaseSensitive, roundTrip.Entries[0].SourcePathCaseSensitive);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapRetainsDistinctQualifiedOwnersAtSameSourcePosition()
    {
        // Public map contract: two emitted owners may report the same authored span.
        // This isolates deduplication; it does not claim an actual artifact stack.
        var map = new PowerShellCompilationFailureMap
        {
            Entries = new[] { (Id: "root", Type: "PowerForge.Compiled.NativeRoot", Name: "<script>"),
                              (Id: "function", Type: "PowerForge.Compiled.Functions", Name: "Invoke") }
                .Select(owner => new PowerShellCompilationFailureMapEntry
                {
                    DocumentId = "owned-document", RelativePath = "input.ps1", UnitId = owner.Id, UnitName = owner.Name,
                    GeneratedMemberName = "Invoke", GeneratedDeclaringTypeName = owner.Type, SourcePathCaseSensitive = true,
                    SourceStartLine = 1, SourceStartColumn = 1, SourceEndLine = 1, SourceEndColumn = 20,
                    GeneratedStartLine = 1, GeneratedEndLine = 1
                }).ToArray()
        };
        var manifest = new PowerShellCompilationArtifactManifest { FailureMap = map };
        var root = "at PowerForge.Compiled.NativeRoot.Invoke() in owned-document:line 1";
        var function = "at PowerForge.Compiled.Functions.Invoke() in owned-document:line 1";
        var failure = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, root + "\n" + function + "\n" + root);
        Assert.Equal(new[] { "function", "root" }, failure.Locations.Select(location => location.UnitId).OrderBy(static id => id, StringComparer.Ordinal));
        Assert.All(failure.Locations, location => Assert.Equal(("input.ps1", 1, 1), (location.RelativePath, location.Line, location.Column)));

        // A hosted PowerShell function remains more specific than its broad script
        // source owner when the diagnostic has no CLR declaring-type identity.
        map.Entries[0].GeneratedDeclaringTypeName = null;
        map.Entries[0].GeneratedStartLine = 0;
        map.Entries[0].SourceEndLine = 5;
        map.Entries[1].GeneratedDeclaringTypeName = null;
        map.Entries[1].GeneratedStartLine = 0;
        Assert.Equal("function", Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            "in owned-document:line 1").Locations).UnitId);
        map.Entries[0].SourceEndLine = 1;
        Assert.Equal("function", Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
            "in owned-document:line 1").Locations).UnitId);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapRecordsClrDeclaringTypeWithoutCSharpIdentifierEscapes()
    {
        using var fixture = ArtifactFixture.Create("function Get-Owned { param([int] $Value) return $Value }");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath,
            "PowerForge.class", "static", "net10.0");
        var method = Assert.Single(typed.Methods);
        Assert.Equal("PowerForge.@class", typed.NamespaceName);
        Assert.Equal("__static", typed.TypeName);
        Assert.Equal("PowerForge.class.__static", method.GeneratedDeclaringTypeName);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapBindsRecursiveNativeBodiesWithoutDuplicatingParentSegments()
    {
        using var fixture = ArtifactFixture.Create("""
            param([int] $Value = 3)
            function Child {
                param([int] $Number)
                function Grandchild { param([int] $Nested) return $Nested + 1 }
                return Grandchild $Number
            }
            Child $Value
            """);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(input.Kind, input.Mode,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, input.Mode, "net10.0",
            PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        Assert.NotNull(entry);
        var root = entry.EntryPoint.Method;
        var child = Assert.Single(root.NestedBodies, body => body.DeclarationName == "Child");
        var grandchild = Assert.Single(root.NestedBodies, body => body.DeclarationName == "Grandchild");
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        Assert.Equal(2, ledger.AnalyzedUnits);
        Assert.Equal(2, ledger.EmittedUnits);
        var owner = Assert.Single(ledger.Entries, unit => unit.Name == "Child");
        Assert.Same(owner, ledger.GetEmittedBodyOwner(child));
        Assert.Same(owner, ledger.GetEmittedBodyOwner(grandchild));
        var sharedEntries = new HashSet<PowerShellCompilationSourceMapEntry>();
        foreach (var body in root.NestedBodies)
            foreach (var segment in body.SourceMap)
            {
                Assert.Contains(root.SourceMap, candidate => ReferenceEquals(candidate, segment));
                Assert.True(sharedEntries.Add(segment), "Subordinate bodies must have disjoint own-map references.");
            }
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger);
        var manifest = new PowerShellCompilationArtifactManifest { FailureMap = map };
        foreach (var body in new[] { child, grandchild })
        {
            var ownEntries = map.Entries.Where(segment => segment.GeneratedMemberName == body.GeneratedMemberName &&
                segment.GeneratedDeclaringTypeName == body.GeneratedDeclaringTypeName).ToArray();
            Assert.Equal(body.SourceMap.Length, ownEntries.Length);
            Assert.NotEmpty(ownEntries);
            Assert.All(ownEntries, segment => Assert.Equal(owner.UnitId, segment.UnitId));
            // Synthetic selector evidence complements the actual artifact test below.
            var segment = ownEntries[0];
            Assert.Equal(owner.UnitId, Assert.Single(PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest,
                $"at {body.GeneratedDeclaringTypeName}.{body.GeneratedMemberName}() in {body.DocumentId}:line {segment.SourceStartLine}").Locations).UnitId);
        }
        Assert.Equal(root.SourceMap.Length - sharedEntries.Count,
            map.Entries.Count(segment => segment.GeneratedMemberName == root.GeneratedName &&
                segment.GeneratedDeclaringTypeName == root.GeneratedDeclaringTypeName));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapRecordsNativeDeclarationsWhenStandaloneClrNamesCollide()
    {
        using var fixture = ArtifactFixture.Create("function Foo-Bar { param([int] $Value) return $Value + 1 }; function Foo_Bar { param([int] $Value) return $Value + 2 }; Foo-Bar 3; Foo_Bar 4");
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(input.Kind, input.Mode,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, input.Mode, "net10.0",
            PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        Assert.True(plan.CanProceed);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        Assert.NotNull(entry);
        Assert.Empty(shaped.Methods);
        Assert.NotNull(entry.EntryPointMethod.NativeFunctionBinding);
        Assert.Equal(2, entry.EntryPointMethod.NestedBodies.Length);
        var methods = PowerShellCompilationExplainShaper.GetEmittedMethods(shaped, entry);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(plan, input.Kind, shaped,
            fixture.ScriptPath, emittedMethods: methods);
        Assert.Equal(3, ledger.AnalyzedUnits);
        Assert.Equal(3, ledger.EmittedUnits);
        Assert.All(ledger.Entries, owner =>
        {
            Assert.True(owner.EmittedClrMethod);
            Assert.True(owner.UsesNativeFunctionBinding);
            Assert.False(owner.RetainedHostedSource);
        });
        var map = PowerShellCompilationDiagnosticsEvidenceBuilder.CreateFailureMap(plan, methods, ledger);
        foreach (var functionName in new[] { "Foo-Bar", "Foo_Bar" })
        {
            var owner = Assert.Single(ledger.Entries, unit => unit.Name == functionName);
            var body = Assert.Single(entry.EntryPointMethod.NestedBodies, body => body.DeclarationName == functionName);
            Assert.Same(owner, ledger.GetEmittedBodyOwner(body));
            Assert.Equal(body.GeneratedMemberName, owner.GeneratedMemberName);
            Assert.Contains(owner.BoundaryCauses, cause => cause.Contains("private emitted native body", StringComparison.Ordinal));
            var segments = map.Entries.Where(segment => segment.GeneratedMemberName == body.GeneratedMemberName &&
                segment.GeneratedDeclaringTypeName == body.GeneratedDeclaringTypeName).ToArray();
            Assert.NotEmpty(segments);
            Assert.All(segments, segment =>
            {
                Assert.Equal(owner.UnitId, segment.UnitId);
                Assert.Equal("TypedClr+PowerShellRuntime", segment.BoundaryContract);
            });
        }
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailureMapMapsActualNativeRootAndCompiledFunctionErrorFrames()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string] $Value = 'invalid-integer', [switch] $FailRoot)
            function Invoke {
                [CmdletBinding()] param([string] $Text)
                return [int]::Parse($Text)
            }
            try {
                if ($FailRoot) { [int]::Parse($Value) }
                else { Invoke -Text $Value -ErrorAction Stop }
            }
            catch { $_.Exception.ToString(); throw }
            """);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeFailureMap", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Hybrid, "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = result.Manifest!;
        var root = Assert.Single(manifest.UnitDispositionLedger!.Entries, unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        var function = Assert.Single(manifest.UnitDispositionLedger.Entries, unit => unit.Name == "Invoke");
        Assert.True(root.EmittedClrMethod);
        Assert.False(root.RetainedHostedSource);
        Assert.True(function.EmittedClrMethod);
        Assert.False(function.RetainedHostedSource);
        var rootType = Assert.Single(manifest.FailureMap!.Entries.Where(segment => segment.UnitId == root.UnitId)
            .Select(segment => segment.GeneratedDeclaringTypeName).Distinct(StringComparer.Ordinal));
        Assert.Equal("PowerForge.Compiled.CompiledNativeScriptEntry", rootType);
        Assert.Contains(manifest.FailureMap.Entries, segment => segment.UnitId == function.UnitId &&
            segment.GeneratedDeclaringTypeName == rootType && segment.GeneratedMemberName != root.GeneratedMemberName);
        var observedOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var failRoot in new[] { true, false })
        {
            // One saved artifact exercises both real authored owners. A helper error
            // crosses SMA and need not retain its caller's root CLR frame.
            var run = failRoot ? RunProcess(result.ArtifactPath!, "-FailRoot") : RunProcess(result.ArtifactPath!);
            var actualFailure = run.StandardOutput + Environment.NewLine + run.StandardError;
            Assert.True(run.ExitCode == 1, "Expected exit1. Actual stdout/stderr: " + actualFailure);
            Assert.True(actualFailure.Contains("FormatException", StringComparison.Ordinal), actualFailure);
            var expectedOwner = failRoot ? root : function;
            Assert.True(manifest.FailureMap.Entries.Where(segment => segment.UnitId == expectedOwner.UnitId)
                .Any(segment => actualFailure.Contains(segment.GeneratedDeclaringTypeName + "." + segment.GeneratedMemberName + "(", StringComparison.Ordinal)),
                "The real exception must contain this authored owner's recorded CLR identity. Actual stdout/stderr: " + actualFailure);
            var mapped = PowerShellCompilationFailureMapper.MapRuntimeFailure(manifest, actualFailure);
            Assert.True(mapped.Locations.Any(location => location.UnitId == expectedOwner.UnitId),
                "The real exception must map to its exact authored owner. Actual stdout/stderr: " + actualFailure);
            Assert.All(mapped.Locations, location => Assert.Contains(manifest.UnitDispositionLedger.Entries, owner => owner.UnitId == location.UnitId));
            observedOwners.Add(expectedOwner.UnitId);
        }
        Assert.Equal(new[] { function.UnitId, root.UnitId }.OrderBy(static id => id, StringComparer.Ordinal),
            observedOwners.OrderBy(static id => id, StringComparer.Ordinal));

        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        var entry = manifest.FailureMap!.Entries.First(segment => segment.GeneratedDeclaringTypeName is not null);
        var type = entry.GeneratedDeclaringTypeName;
        entry.GeneratedDeclaringTypeName = type + ".Tampered";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
        entry.GeneratedDeclaringTypeName = type;
        var casePolicy = Assert.IsType<bool>(entry.SourcePathCaseSensitive);
        entry.SourcePathCaseSensitive = !casePolicy;
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest));
        entry.SourcePathCaseSensitive = casePolicy;
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
    }
}
