namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Shape_GuardedExecutableDeclarationsRemainCompiledWithoutStartupCommandRegistration()
    {
        using var fixture = ArtifactFixture.Create("""
            function NotACmdlet { param([int]$Value) return $Value + 1 }
            $before = Get-Command -Name Read-LateValue -ErrorAction SilentlyContinue
            $helperPath = Join-Path $PSScriptRoot 'Helper.ps1'
            . $helperPath
            Read-LateValue -Value 7
            """, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "function Read-LateValue { param([int]$Value) return $Value + 1 }");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath, helper }, "PowerForge.Compiled", "DeclarationTiming", "net10.0",
            PowerShellCompilationCapabilities.HybridExecutable, guardedExecutableEntryPath: fixture.ScriptPath);
        Assert.Equal(new[] { helper }, typed.NativeDeclarationTimingSourcePaths);
        Assert.Equal(fixture.ScriptPath, typed.GuardedExecutableEntryPath);
        Assert.NotNull(Assert.Single(typed.Methods, static method => method.SourceName == "Read-LateValue").NativeFunctionBinding);

        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable);
        Assert.Contains(routed.Methods, static method => method.SourceName == "Read-LateValue");
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(routed, exportedFunctions: null,
            targetFramework: "net10.0", semanticProfileId: PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            capabilities: PowerShellCompilationCapabilities.HybridExecutable);

        Assert.Equal(new[] { helper }, prepared.NativeDeclarationTimingSourcePaths);
        Assert.Equal(fixture.ScriptPath, prepared.GuardedExecutableEntryPath);
        var preserved = Assert.Single(prepared.Methods, static method => method.SourceName == "Read-LateValue");
        Assert.NotNull(preserved.NativeFunctionBinding);
        Assert.Empty(PowerShellBinaryCmdletSourceGenerator.GetCommandIdentities(prepared, exportedFunctions: null));
        Assert.Contains(prepared.Diagnostics, static diagnostic => diagnostic.Message.Contains("Verb-Noun naming", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(". \"$PSScriptRoot/Helper.ps1\"; Read-LateValue -Value 7", "$script:state = 1; function Read-LateValue { param([int]$Value) return $Value + 1 }")]
    [InlineData("if ($true) { . \"$PSScriptRoot/Helper.ps1\" }; Read-LateValue -Value 7", "function Read-LateValue { param([int]$Value) return $Value + 1 }")]
    [InlineData(". \"$PSScriptRoot/Helper.ps1\"; Read-LateValue -Value 7", "param([string]$Name); function Read-LateValue { param([int]$Value) return $Value + 1 }")]
    public void Shape_ExecutableTimingQualificationDoesNotAdmitOtherSourceInitialization(string root, string helperSource)
    {
        using var fixture = ArtifactFixture.Create(root, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, helperSource);
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath, helper }, "PowerForge.Compiled", "OtherInitialization", "net10.0",
            PowerShellCompilationCapabilities.HybridExecutable, guardedExecutableEntryPath: fixture.ScriptPath);

        Assert.Empty(typed.NativeDeclarationTimingSourcePaths);
        Assert.Equal(fixture.ScriptPath, typed.GuardedExecutableEntryPath);
        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable);
        Assert.DoesNotContain(routed.Methods, static method => method.SourceName == "Read-LateValue");
        Assert.Contains(routed.Diagnostics, static diagnostic => diagnostic.Message.Contains("command-availability timing", StringComparison.Ordinal));
    }

    [Fact]
    public void Shape_GuardedExecutableTimingContextDoesNotChangeBinaryModulePolicy()
    {
        using var fixture = ArtifactFixture.Create(". \"$PSScriptRoot/Helper.ps1\"; Read-LateValue -Value 7", ".psm1", compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "function Read-LateValue { param([int]$Value) return $Value + 1 }");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath, helper }, "PowerForge.Compiled", "ModuleTiming", "net10.0",
            PowerShellCompilationCapabilities.HybridModule, guardedExecutableEntryPath: fixture.ScriptPath);

        Assert.Empty(typed.NativeDeclarationTimingSourcePaths);
        Assert.Null(typed.GuardedExecutableEntryPath);
        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(routed.Methods, static method => method.SourceName == "Read-LateValue");
    }
}
