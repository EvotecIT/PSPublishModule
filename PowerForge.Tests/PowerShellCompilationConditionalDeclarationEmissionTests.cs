namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void ConditionalDeclarations_EmissionKeepsFactoryCallsReachableWithoutChangingCmdletOrCallbackOwnership()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-OrdinaryValue { param([int] $Number) return $Number + 1 }
            if ($true) {
                function Get-ConditionalValue {
                    param([int] $Value)
                    $callback = { param([int] $Item) return $Item + 2 }
                    return & $callback $Value
                }
            }
            else {
                function Get-ConditionalValue { trap { continue }; return 'retained' }
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.ConditionalEmission", "Methods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var routed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridModule);
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(routed, exportedFunctions: null,
            targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(2, prepared.Methods.Length);
        var conditional = Assert.Single(prepared.Methods, static method => method.SourceName == "Get-ConditionalValue");
        Assert.NotNull(conditional.NativeFunctionBinding);
        Assert.NotNull(conditional.ConditionalDeclarationOffset);
        Assert.Null(Assert.Single(prepared.Methods, static method => method.SourceName == "Get-OrdinaryValue").NativeFunctionBinding);
        Assert.Single(PowerShellBinaryCmdletSourceGenerator.GetCommandIdentities(prepared, exportedFunctions: null));

        // The generated factory is a sibling C# type, while callback helpers stay inside the method container.
        Assert.Contains("    internal static ", prepared.SourceCode, StringComparison.Ordinal);
        Assert.Contains("    private static ", prepared.SourceCode, StringComparison.Ordinal);
        var generated = PowerShellBinaryCmdletSourceGenerator.Generate(prepared, targetFramework: "net10.0");
        Assert.Contains("class GetOrdinaryValueCommand", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("class GetConditionalValueCommand", generated, StringComparison.Ordinal);
        Assert.Contains(PowerShellNativeFunctionSourceGenerator.FactoryMethod(conditional), generated, StringComparison.Ordinal);
        var composed = PowerShellHybridModuleComposer.ComposeRoot(fixture.ScriptPath, "ConditionalEmission.dll", prepared);
        Assert.Contains("trap { continue }; return 'retained'", composed, StringComparison.Ordinal);
    }
}
