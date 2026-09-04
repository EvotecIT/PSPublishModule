using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string HybridTypedModuleStateAccessSource = """
        $script:Text = 'alpha'
        $script:Items = [string[]]@('one', 'two')
        function Get-TypedStateLength {
            [CmdletBinding()]
            param()
            return ([string]$script:Text).Length
        }
        function Get-TypedStateItem {
            [CmdletBinding()]
            param([int] $Index)
            return ([string[]]$script:Items)[$Index]
        }
        function Set-TypedStateText {
            [CmdletBinding()]
            param([AllowNull()][object] $Value)
            $script:Text = $Value
        }
        function Set-TypedStateItems {
            [CmdletBinding()]
            param([AllowNull()][object] $Value)
            $script:Items = $Value
        }
        Export-ModuleMember -Function Get-TypedStateLength, Get-TypedStateItem, Set-TypedStateText, Set-TypedStateItems
        """;

    [Fact]
    public void Compile_AuthoredModuleStateConversionOwnsTypedAccessAndEvaluatesIndexInputsOnce()
    {
        var document = PowerShellSourceParser.Parse(
            HybridTypedModuleStateAccessSource,
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "hybrid-typed-module-state-access.psm1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var lengthGetter = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-TypedStateLength");
        var lengthMember = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(lengthGetter.Body.Statements)).Expression);
        var lengthConversion = Assert.IsType<PowerShellBoundConversionExpression>(lengthMember.Receiver);
        Assert.Equal(PowerShellTypeFactProvenance.Explicit, lengthConversion.Type.Provenance);
        Assert.Equal(
            PowerShellRuntimeStateIntrinsicKind.ModuleVariable,
            Assert.IsType<PowerShellBoundRuntimeStateExpression>(lengthConversion.Operand).Kind);

        var getter = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-TypedStateItem");
        var index = Assert.IsType<PowerShellBoundIndexExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(getter.Body.Statements)).Expression);
        var conversion = Assert.IsType<PowerShellBoundConversionExpression>(index.Target);
        Assert.Equal(PowerShellTypeFactProvenance.Explicit, conversion.Type.Provenance);
        Assert.Equal(
            PowerShellRuntimeStateIntrinsicKind.ModuleVariable,
            Assert.IsType<PowerShellBoundRuntimeStateExpression>(conversion.Operand).Kind);

        var loweredGetter = Assert.Single(result.Lowered.Functions, static function => function.Symbol.Name == "Get-TypedStateItem");
        var loweredIndex = Assert.IsType<PowerShellLoweredIndexExpression>(
            Assert.IsType<PowerShellLoweredReturnStatement>(Assert.Single(loweredGetter.Statements)).Expression);
        Assert.StartsWith("__pf_index_target_", loweredIndex.TargetTemporary, StringComparison.Ordinal);
        Assert.StartsWith("__pf_index_key_", loweredIndex.IndexTemporary, StringComparison.Ordinal);
        Assert.NotEqual(loweredIndex.TargetTemporary, loweredIndex.IndexTemporary);

        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_TypedStateItem").Source;
        Assert.Equal(1, CountOccurrences(source, "__readPowerShellModuleVariable(\"Items\")"));
        Assert.Equal(1, CountOccurrences(source, "var " + loweredIndex.TargetTemporary + " ="));
        Assert.Equal(1, CountOccurrences(source, "var " + loweredIndex.IndexTemporary + " ="));
        Assert.True(
            source.IndexOf("var " + loweredIndex.TargetTemporary + " =", StringComparison.Ordinal) <
            source.IndexOf("var " + loweredIndex.IndexTemporary + " =", StringComparison.Ordinal));
        Assert.Equal(
            1,
            CountOccurrences(
                Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_TypedStateLength").Source,
                "__readPowerShellModuleVariable(\"Text\")"));
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_AuthoredModuleStateConversionsPreserveLiveMemberAndIndexSemantics(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(HybridTypedModuleStateAccessSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridTypedModuleStateAccess",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            EmitSource = true,
            EmitIrSnapshots = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        var item = Assert.Single(ledger.Entries, static entry => entry.Name == "Get-TypedStateItem");
        Assert.True(item.RuntimeRouted);
        Assert.False(item.ShapingFallback);
        Assert.Equal(1, item.ModuleStateReadBoundaryCrossings);
        Assert.Contains(item.BoundaryCauses, static cause =>
            cause.Contains("$script:Items", StringComparison.Ordinal));

        const string proof =
            "'length:' + (Get-TypedStateLength); " +
            "'first:' + (Get-TypedStateItem -Index 0); " +
            "'last:' + (Get-TypedStateItem -Index -1); " +
            "$value = Get-TypedStateItem -Index 4; if ($null -eq $value) { 'missing:null' } else { 'missing:' + $value }; " +
            "Set-TypedStateText -Value $null; 'empty-length:' + (Get-TypedStateLength); " +
            "Set-TypedStateItems -Value 'scalar'; 'scalar:' + (Get-TypedStateItem -Index 0); " +
            "Set-TypedStateItems -Value $null; try { Get-TypedStateItem -Index 0; 'null:missed' } catch { 'null:' + (($_.FullyQualifiedErrorId -split ',')[0]) }";
        var interpreted = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(interpreted, compiled);
        Assert.Equal(new[]
        {
            "length:5",
            "first:one",
            "last:two",
            "missing:null",
            "empty-length:0",
            "scalar:scalar",
            "null:NullArray"
        }, compiled.Split(Environment.NewLine));

    }

    [Theory]
    [InlineData("([string[]]($script:Items))[0]")]
    [InlineData("([string[]](Get-Items))[0]")]
    [InlineData("([string[]]$script:Items)[(Get-Random)]")]
    public void Analyze_CompoundOrEffectfulTypedModuleStateIndexingRemainsFallback(string expression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-StateItem {{ [CmdletBinding()] param() return {expression} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        Assert.False(FindFunction(plan, "Get-StateItem").IsCompilable);
    }

    [Theory]
    [InlineData("return ([object]$script:State).Count")]
    [InlineData("$copy = $script:State; return $copy.Count")]
    [InlineData("return ([string]([object]$script:State)).Length")]
    [InlineData("return ([string]$script:State).Substring(1)")]
    [InlineData("[string[]] $copy = [string[]]$script:State; return $copy[0]")]
    [InlineData("return Use-State -Value $script:State")]
    [InlineData("return (Read-State).Count")]
    [InlineData("return [string]::Concat($script:State)")]
    [InlineData("foreach ($item in ([string[]]$script:State)) { $null = $item }; return 1")]
    [InlineData("([string[]]$script:State) | ForEach-Object { $copy = $_ }; return $copy")]
    public void Analyze_DerivedModuleStateCannotEscapeTheDirectTypedReadBoundary(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Use-State { param([object] $Value) return $Value.Count }; " +
            "function Read-State { return $script:State }; " +
            $"function Get-StateAccess {{ [CmdletBinding()] param() {body} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        var function = FindFunction(plan, "Get-StateAccess");
        Assert.False(function.IsCompilable);
        Assert.NotEmpty(function.Diagnostics);
    }

    [Theory]
    [InlineData("[object]$Value = $script:State; [object]$Output = Write-Output -InputObject $Value; return $Output.Count")]
    [InlineData("[object]$Value = $script:State; Write-Output -InputObject $Value")]
    [InlineData("[object]$Value = $script:State; Write-Output -InputObject $Value; return 1")]
    [InlineData("[object]$Value = $script:State; $null = Write-Output -InputObject $Value; return 1")]
    [InlineData("[object]$Value = $script:State; Write-Output -InputObject $Value 6> $null; return 1")]
    [InlineData("Write-Output -InputObject $script:State; return 1")]
    [InlineData("Write-Output -InputObject ([string[]]$script:State); return 1")]
    public void Analyze_HostedCommandRegionsCannotLaunderModuleStateOrigin(string body)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-StateAccess {{ [CmdletBinding()] param() {body} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        var function = FindFunction(plan, "Get-StateAccess");
        Assert.False(function.IsCompilable);
        Assert.NotEmpty(function.Diagnostics);
    }

    [Fact]
    public void Analyze_HostedLifecyclePipelineCannotConsumeModuleStateDerivedInput()
    {
        using var fixture = ArtifactFixture.Create(
            "function Measure-Total { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value) " +
            "begin { $Total = 0 } process { $Total += $Value } end { return $Total } }; " +
            "function Get-StateTotal { [CmdletBinding()] param() [int[]]$Values = [int[]]$script:State; return $Values | Measure-Total }",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        var function = FindFunction(plan, "Get-StateTotal");
        Assert.False(function.IsCompilable);
        Assert.NotEmpty(function.Diagnostics);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsAuthoredAccessToParentScriptModuleState()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StrictItem { [CmdletBinding()] param() return ([string[]]$script:Items)[0] }; Export-ModuleMember -Function Get-StrictItem",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictTypedModuleStateAccess",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("script:Items", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    private static int CountOccurrences(string text, string value)
        => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
