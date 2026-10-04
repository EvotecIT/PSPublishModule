using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellHostedValueClassTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void DataClassConstructionUsesNativeLexicalTypeAndKeepsStrictClosed(string framework)
    {
        var document = Parse("class OwnedValue { [string]$Name; [long]$Count; [string]$Computer=[Environment]::MachineName }; function New-Value { $value=[OwnedValue]::New(); $value.Name='value'; $value }");
        Assert.Empty(document.Errors);
        Assert.True(PowerShellHostedValueClassPolicy.IsQualified(document, framework, PowerShellCompilationCapabilities.HybridModule));
        var compiled = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(compiled.Lowered.Functions);
        Assert.False(PowerShellHostedValueClassPolicy.IsQualified(document, framework, PowerShellCompilationCapability.None));
        Assert.False(PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document));
    }

    [Theory]
    [InlineData("class OwnedValue { OwnedValue() {} }")]
    [InlineData("class OwnedValue { [string]Method() { return 'value' } }")]
    [InlineData("class OwnedValue { static [string]$Name }")]
    [InlineData("class OwnedValue { [object]$Name }")]
    [InlineData("class OwnedValue { [string]$Name=(Get-Date) }")]
    [InlineData("class OwnedValue : Exception { [string]$Name }")]
    public void DataClassClosureRejectsUnqualifiedDeclarationMembers(string declaration)
        => Assert.False(PowerShellHostedValueClassPolicy.IsQualified(Parse(declaration + "; function New-Value { [OwnedValue]::new() }"),
            "net10.0", PowerShellCompilationCapabilities.HybridModule));

    [Theory]
    [InlineData("[OwnedValue]")]
    [InlineData("[OwnedValue]::new('argument')")]
    [InlineData("[OwnedValue]::Missing()")]
    [InlineData("[OwnedValue]$value=$null")]
    public void DataClassClosureDoesNotAdmitOtherTypeValueConsumers(string body)
        => Assert.False(PowerShellHostedValueClassPolicy.IsQualified(Parse("class OwnedValue { [string]$Name }; function New-Value { " + body + " }"),
            "net10.0", PowerShellCompilationCapabilities.HybridModule));

    private static ParsedSourceDocument Parse(string source)
        => PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "hosted-value-class.psm1"));
}
