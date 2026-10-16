using System.Management.Automation.Language;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string ConditionalDeclarationModule = """
        param([string] $Branch)
        $script:BeforeDeclaration = $null -ne (Get-Command Invoke-ConditionalChoice -ErrorAction Ignore)
        $script:ReplacementTrace = @()
        if ($false) {
            function Invoke-NeverDeclared([int] $Value) { return "never-body:$Value" }
            throw 'An unselected declaration branch was evaluated.'
        }
        if ($Branch -eq 'A') { function Invoke-ConditionalChoice([Alias('n')][ValidateRange(1,9)][int] $Value = 2) { return "body-a:$Value" } } elseif ($Branch -eq 'B') { function Invoke-ConditionalChoice { [CmdletBinding()] param([Alias('n')][ValidateRange(1,9)][int] $Value = 3); return "body-b:$Value" } }
        if ($Branch -eq 'Replace') {
            function Invoke-ConditionalChoice([Alias('n')][ValidateRange(1,9)][int] $Value = 4) {
                return "body-first:$Value"
            }
            $script:ReplacementTrace += Invoke-ConditionalChoice -n 5
            function Invoke-ConditionalChoice([Alias('n')][ValidateRange(1,9)][int] $Value = 6) {
                return "body-second:$Value"
            }
            $script:ReplacementTrace += Invoke-ConditionalChoice -n 5
        }
        $script:AfterDeclaration = $null -ne (Get-Command Invoke-ConditionalChoice -ErrorAction Ignore)
        """;

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ConditionalDeclarations_ShapeOwnsEveryAuthoredBodyWithoutHoistingCommands()
    {
        using var fixture = ArtifactFixture.Create(ConditionalDeclarationModule, ".psm1");
        var ast = Parser.ParseFile(fixture.ScriptPath, out _, out var errors);
        Assert.Empty(errors);
        var declarations = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>().OrderBy(static function => function.Extent.StartOffset).ToArray();
        Assert.Equal(5, declarations.Length);
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(PowerShellCompilationMode.Hybrid,
            new[] { fixture.ScriptPath }, fixture.RootPath, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var units = Assert.Single(plan.Files).Units.Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function).ToArray();
        Assert.Equal(declarations.Select(static function => (function.Name, function.Body.Extent.StartLineNumber)),
            units.Select(static unit => (unit.Name, unit.StartLine)));
        Assert.Equal(2, units.Count(unit => unit.Name == "Invoke-ConditionalChoice" && unit.StartLine == units[1].StartLine));
        var explanation = PowerShellCompilationExplanationService.Create(plan);
        Assert.Equal(5, explanation.Files.SelectMany(static file => file.Units)
            .Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function)
            .Select(static unit => unit.UnitId).Distinct(StringComparer.Ordinal).Count());

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.ConditionalDeclarations", "Methods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(routed, exportedFunctions: null,
            targetFramework: "net10.0", semanticProfileId: PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(5, prepared.Methods.Length);
        Assert.Equal(declarations.Select(static function => (function.Name, function.Body.Extent.StartLineNumber)),
            prepared.Methods.OrderBy(static method => method.SourceLine).Select(static method => (method.SourceName, method.SourceLine)));
        Assert.Equal(5, prepared.Methods.Select(static method => method.GeneratedName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(prepared.Methods, static method => Assert.NotNull(method.NativeFunctionBinding));
        Assert.Empty(PowerShellBinaryCmdletSourceGenerator.GetCommandIdentities(prepared, exportedFunctions: null));

        var composed = PowerShellHybridModuleComposer.ComposeRoot(fixture.ScriptPath, "ConditionalDeclarations.dll", prepared);
        Assert.Contains("if ($Branch -eq 'A')", composed, StringComparison.Ordinal);
        Assert.Contains("elseif ($Branch -eq 'B')", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("body-a:$Value", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("body-b:$Value", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("body-first:$Value", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("body-second:$Value", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("never-body:$Value", composed, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ConditionalDeclarations_ShapePreservesUnsupportedSameNameSibling()
    {
        using var fixture = ArtifactFixture.Create("""
            param([bool] $UseCompiled)
            if ($UseCompiled) {
                function Invoke-MixedChoice([int] $Value) { return "compiled-body:$Value" }
            }
            else {
                function Invoke-MixedChoice([int] $Value) { trap { continue }; return "retained-body:$Value" }
            }
            """, ".psm1");
        var plan = new PowerShellCompilationAnalyzer().AnalyzeFiles(PowerShellCompilationMode.Hybrid,
            new[] { fixture.ScriptPath }, fixture.RootPath, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.MixedConditionalDeclarations", "Methods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(routed, exportedFunctions: null,
            targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal("Invoke-MixedChoice", Assert.Single(prepared.Methods).SourceName);
        var ledger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
            plan, PowerShellCompilationArtifactKind.BinaryModule, prepared, fixture.ScriptPath);
        var owned = ledger.Entries.Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function).ToArray();
        Assert.Equal(2, owned.Length);
        Assert.Equal(2, owned.Select(static unit => unit.UnitId).Distinct(StringComparer.Ordinal).Count());
        var compiled = Assert.Single(owned, static unit => unit.EmittedClrMethod);
        Assert.False(compiled.RetainedHostedSource);
        Assert.False(compiled.ShapingFallback);
        var retained = Assert.Single(owned, static unit => !unit.EmittedClrMethod);
        Assert.False(retained.SemanticEligible);
        Assert.True(retained.RetainedHostedSource);
        Assert.False(retained.Omitted);
        Assert.False(retained.Rejected);
        var composed = PowerShellHybridModuleComposer.ComposeRoot(fixture.ScriptPath, "MixedConditionalDeclarations.dll", prepared);
        Assert.DoesNotContain("compiled-body:$Value", composed, StringComparison.Ordinal);
        Assert.Contains("retained-body:$Value", composed, StringComparison.Ordinal);
        Assert.Contains("trap { continue }", composed, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ConditionalDeclarations_ArtifactPreservesSelectedBodyReplacementAndNativeMetadata(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(ConditionalDeclarationModule, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ConditionalDeclarations",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        var owned = manifest.UnitDispositionLedger!.Entries.Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function).ToArray();
        Assert.Equal(5, owned.Length);
        Assert.Equal(4, owned.Count(static unit => unit.Name == "Invoke-ConditionalChoice"));
        Assert.Equal(5, owned.Select(static unit => unit.UnitId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, owned.Select(static unit => unit.GeneratedMemberName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(owned, static unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
            Assert.False(unit.ShapingFallback);
            Assert.False(unit.Omitted);
            Assert.False(unit.Rejected);
            Assert.False(unit.EmittedBinaryCmdlet);
        });
        var initializer = Assert.Single(manifest.UnitDispositionLedger.Entries, static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        Assert.True(initializer.RetainedHostedSource);

        const string observation = """
            & $module {
                $command = Get-Command Invoke-ConditionalChoice -ErrorAction Ignore
                $never = Get-Command Invoke-NeverDeclared -ErrorAction Ignore
                $records = @()
                $default = @()
                $validationError = $null
                $parameterType = $null
                $aliases = @()
                $advanced = $null
                if ($null -ne $command) {
                    $records = @(Invoke-ConditionalChoice -n 5)
                    $default = @(Invoke-ConditionalChoice)
                    $parameterType = $command.Parameters['Value'].ParameterType.FullName
                    $aliases = @($command.Parameters['Value'].Aliases)
                    $advanced = $command.CmdletBinding
                    try { Invoke-ConditionalChoice -n 20 -ErrorAction Stop | Out-Null }
                    catch { $validationError = $_.FullyQualifiedErrorId }
                }
                [ordered]@{
                    branch=$Branch; before=$script:BeforeDeclaration; after=$script:AfterDeclaration
                    neverAvailable=($null -ne $never); records=$records; default=$default
                    replacement=@($script:ReplacementTrace); parameterType=$parameterType
                    aliases=$aliases; advanced=$advanced; validationError=$validationError
                } | ConvertTo-Json -Depth 8 -Compress
            }
            """;
        foreach (var branch in new[] { "A", "B", "None", "Replace" })
        {
            var original = RunStatementErrorProbe(host,
                "$ErrorActionPreference='Stop'; $module=@(Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
                "' -Force -PassThru -ArgumentList '" + branch + "')[-1]; " + observation,
                fixture.RootPath, "conditional-original-" + branch);
            Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
            using (var expected = JsonDocument.Parse(original.StandardOutput))
            {
                Assert.False(expected.RootElement.GetProperty("before").GetBoolean());
                Assert.False(expected.RootElement.GetProperty("neverAvailable").GetBoolean());
                Assert.Equal(branch != "None", expected.RootElement.GetProperty("after").GetBoolean());
                if (branch != "None")
                {
                    var body = branch switch { "A" => "body-a", "B" => "body-b", _ => "body-second" };
                    var defaultValue = branch switch { "A" => 2, "B" => 3, _ => 6 };
                    Assert.Equal(new[] { body + ":5" }, expected.RootElement.GetProperty("records").EnumerateArray().Select(static value => value.GetString()));
                    Assert.Equal(new[] { body + ":" + defaultValue }, expected.RootElement.GetProperty("default").EnumerateArray().Select(static value => value.GetString()));
                    Assert.Equal("System.Int32", expected.RootElement.GetProperty("parameterType").GetString());
                    Assert.Contains("n", expected.RootElement.GetProperty("aliases").EnumerateArray().Select(static value => value.GetString()));
                    Assert.Equal(branch == "B", expected.RootElement.GetProperty("advanced").GetBoolean());
                    Assert.False(string.IsNullOrWhiteSpace(expected.RootElement.GetProperty("validationError").GetString()));
                }
                else
                {
                    Assert.Empty(expected.RootElement.GetProperty("records").EnumerateArray());
                    Assert.Empty(expected.RootElement.GetProperty("default").EnumerateArray());
                }
                if (branch == "Replace")
                    Assert.Equal(new[] { "body-first:5", "body-second:5" }, expected.RootElement.GetProperty("replacement").EnumerateArray().Select(static value => value.GetString()));
            }
            var withdrawn = fixture.ScriptPath + ".withdrawn";
            File.Move(fixture.ScriptPath, withdrawn);
            try
            {
                var compiled = RunStatementErrorProbe(host,
                    "$ErrorActionPreference='Stop'; $module=@(Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
                    "' -Force -PassThru -ArgumentList '" + branch + "')[-1]; " + observation,
                    fixture.RootPath, "conditional-compiled-" + branch);
                Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
                Assert.Equal(original.StandardOutput, compiled.StandardOutput);
                Assert.Equal(original.StandardError, compiled.StandardError);
            }
            finally { File.Move(withdrawn, fixture.ScriptPath); }
        }
    }
}
