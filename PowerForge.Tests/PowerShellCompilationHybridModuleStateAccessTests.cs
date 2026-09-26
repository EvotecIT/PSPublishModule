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
        function Get-TypedStateLengthLowercase {
            [CmdletBinding()]
            param()
            return ([string]$script:Text).length
        }
        function Get-TypedStateLastLiteral {
            [CmdletBinding()]
            param()
            return ([string[]]$script:Items)[-1]
        }
        function Get-TypedStateLastSpaced {
            [CmdletBinding()]
            param()
            return ([string[]]$script:Items)[ - 1 ]
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
        Export-ModuleMember -Function Get-TypedStateLength, Get-TypedStateItem, Get-TypedStateLengthLowercase, Get-TypedStateLastLiteral, Get-TypedStateLastSpaced, Set-TypedStateText, Set-TypedStateItems
        """;

    [Fact]
    public void Compile_AuthoredModuleStateStringConversionsUseNativeInvocation()
    {
        var document = PowerShellSourceParser.Parse(
            HybridTypedModuleStateAccessSource,
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "hybrid-typed-module-state-access.psm1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        foreach (var name in new[] { "Get-TypedStateLength", "Get-TypedStateItem", "Get-TypedStateLengthLowercase", "Get-TypedStateLastLiteral", "Get-TypedStateLastSpaced" })
        {
            Assert.NotNull(Assert.Single(result.Analyzed.Functions, function => function.Symbol.Name == name).NativeFunctionBinding);
            Assert.NotNull(Assert.Single(result.Emitted.Methods, method => method.GeneratedName == name.Replace('-', '_')).NativeFunctionBinding);
        }
    }

    [Theory]
    [InlineData("return ([string]$script:Text).Length")]
    [InlineData("return ([string[]]$script:Items)[0]")]
    public void Compile_AuthoredModuleStateStringConversionsRemainGuardedWithoutNativeBinding(string body)
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-State { [CmdletBinding()] param() " + body + " }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "hybrid-guarded-module-state-access.psm1"));
        var capabilities = PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding;
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", capabilities);

        Assert.Contains(result.Emitted.Diagnostics, static diagnostic =>
            diagnostic.Code == PowerShellStringificationScopePolicy.DiagnosticCode);
        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Get_State");
    }

    [Theory]
    [InlineData("[string] $copy = [string]$script:Text; return $copy.Length")]
    [InlineData("return ([string]$script:Text).Substring(1)")]
    public void Analyze_DerivedModuleStateStringConversionDoesNotSelectNativeRead(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-State { [CmdletBinding()] param() " + body + " }", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Analyze, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule));

        var function = FindFunction(plan, "Get-State");
        Assert.False(function.IsCompilable);
        Assert.NotEmpty(function.Diagnostics);
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
        Assert.Equal(7, result.Manifest!.CompiledMethods);
        var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
        var item = Assert.Single(ledger.Entries, static entry => entry.Name == "Get-TypedStateItem");
        Assert.True(item.RuntimeRouted);
        Assert.False(item.ShapingFallback);
        Assert.True(item.UsesNativeFunctionBinding);
        Assert.Equal(0, item.ModuleStateReadBoundaryCrossings);

        const string proof =
            "'length:' + (Get-TypedStateLength); " +
            "'first:' + (Get-TypedStateItem -Index 0); " +
            "'last:' + (Get-TypedStateItem -Index -1); " +
            "'lowercase-length:' + (Get-TypedStateLengthLowercase); 'literal-last:' + (Get-TypedStateLastLiteral); 'spaced-last:' + (Get-TypedStateLastSpaced); " +
            "$value = Get-TypedStateItem -Index 4; if ($null -eq $value) { 'missing:null' } else { 'missing:' + $value }; " +
            "Set-TypedStateText -Value $null; 'empty-length:' + (Get-TypedStateLength); " +
            "$global:callbackHits=0; $token=[pscustomobject]@{}; Add-Member -InputObject $token -MemberType ScriptMethod -Name ToString -Force -Value { $global:callbackHits++; 'token' }; " +
            "Set-TypedStateText -Value $token; 'callback-length:' + (Get-TypedStateLength); 'callback-count:' + $global:callbackHits; " +
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
            "lowercase-length:5",
            "literal-last:two",
            "spaced-last:two",
            "missing:null",
            "empty-length:0",
            "callback-length:5",
            "callback-count:1",
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
        // CLR-owned storage still cannot carry opaque module state across a hosted boundary.
        // Native invocation storage has its own qualified live-state contract.
        var capabilities = PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding;
        using var fixture = ArtifactFixture.Create(
            $"function Get-StateAccess {{ [CmdletBinding()] param() {body} }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Analyze,
            targetFramework: "net10.0",
            capabilities: capabilities));

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

}
