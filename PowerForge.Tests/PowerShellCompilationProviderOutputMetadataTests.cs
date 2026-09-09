using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryProviderOutputMetadataUsesResultContract(bool collection)
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.Providers = new[]
        {
            Provider("generic.command.output.int32", "Write-PackageInt32Core", "Success",
                collection ? "ParseInt32Many" : "ParseInt32",
                collection ? PowerShellCompilationCommandOutput.Enumerated : PowerShellCompilationCommandOutput.Projected,
                collection ? PowerShellCompilationCommandCardinality.Collection : PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Int32)
        };
        providerFixture.Manifest.Providers[0].ModuleNames = new[] { "Generic.Semantic.Provider" };
        using var fixture = ScriptFixture.Create("""
            function Get-Number { [CmdletBinding()] param(); Generic.Semantic.Provider\Write-PackageInt32Core '42' }
            function Get-Mixed { [CmdletBinding()] param(); Generic.Semantic.Provider\Write-PackageInt32Core '7'; 'text' }
            """);
        var typed = new PowerShellTypedCompilationTranspiler(providerFixture.Manifest.Providers)
            .TranspileForBinaryModule(new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ProviderOutputMethods", "net8.0");
        Assert.Empty(typed.Diagnostics);
        Assert.True(typed.Methods.All(method => method.SuccessOutputType.Length > 0), typed.SourceCode);
        Assert.Equal("System.Int32", Assert.Single(typed.Methods, method => method.SourceName == "Get-Number").SuccessOutputType);
        Assert.Equal("System.Object", Assert.Single(typed.Methods, method => method.SourceName == "Get-Mixed").SuccessOutputType);
        var source = PowerShellBinaryCmdletSourceGenerator.Generate(typed, new[] { "Get-Number", "Get-Mixed" }, "net8.0");
        Assert.Contains("[OutputType(typeof(int))]", source);
        Assert.Contains("[OutputType(typeof(object))]", source);
    }
}
