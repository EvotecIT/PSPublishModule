using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public void BinaryHostedOutputMetadataIncludesUnknownRecordsButExcludesCapturedOutput()
    {
        using var fixture = ScriptFixture.Create("""
            function Get-HostedMixed { [CmdletBinding()] param(); Get-UnknownMetadataRecord; 'text' }
            function Get-HostedOnly { [CmdletBinding()] param(); Get-UnknownMetadataRecord }
            function Get-CapturedOnly { [CmdletBinding()] param(); [int] $value = Get-UnknownMetadataRecord; 'text' }
            """);
        var typed = new PowerShellTypedCompilationTranspiler()
            .TranspileForBinaryModule(new[] { fixture.ScriptPath }, "PowerForge.Compiled", "HostedOutputMethods", "net10.0");
        Assert.Empty(typed.Diagnostics);
        Assert.Equal("System.Object", Assert.Single(typed.Methods, method => method.SourceName == "Get-HostedMixed").SuccessOutputType);
        Assert.Equal("System.Object", Assert.Single(typed.Methods, method => method.SourceName == "Get-HostedOnly").SuccessOutputType);
        Assert.Equal("System.String", Assert.Single(typed.Methods, method => method.SourceName == "Get-CapturedOnly").SuccessOutputType);
        var source = PowerShellBinaryCmdletSourceGenerator.Generate(typed,
            new[] { "Get-HostedMixed", "Get-HostedOnly", "Get-CapturedOnly" }, "net10.0");
        Assert.Equal(2, source.Split("[OutputType(typeof(object))]", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, source.Split("[OutputType(typeof(string))]", StringSplitOptions.None).Length - 1);
    }

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
            .TranspileForBinaryModule(new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ProviderOutputMethods", "net10.0");
        Assert.Empty(typed.Diagnostics);
        Assert.True(typed.Methods.All(method => method.SuccessOutputType.Length > 0), typed.SourceCode);
        Assert.Equal("System.Int32", Assert.Single(typed.Methods, method => method.SourceName == "Get-Number").SuccessOutputType);
        Assert.Equal("System.Object", Assert.Single(typed.Methods, method => method.SourceName == "Get-Mixed").SuccessOutputType);
        var source = PowerShellBinaryCmdletSourceGenerator.Generate(typed, new[] { "Get-Number", "Get-Mixed" }, "net10.0");
        Assert.Contains("[OutputType(typeof(int))]", source);
        Assert.Contains("[OutputType(typeof(object))]", source);
    }
}
