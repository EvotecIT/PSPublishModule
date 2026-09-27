using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellHostedAttributeDeclarationTests
{
    private const string Transform = """
        class AuthoredTransform : System.Management.Automation.ArgumentTransformationAttribute {
            [object] Transform([System.Management.Automation.EngineIntrinsics]$Context, [object]$Input) { return $Input }
        }
        """;
    private const string Consumer = "function Read-Value { param([AuthoredTransform()][string]$Value) process { $Value } }";

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void HostedAttributeClosureKeepsNativeParameterMetadataAndStrictClosed(string framework)
    {
        var document = Parse(Transform + "\n" + Consumer);
        Assert.Empty(document.Errors);
        Assert.True(PowerShellHostedAttributeDeclarationPolicy.IsQualified(document, framework, PowerShellCompilationCapabilities.HybridModule));
        Assert.Empty(PowerShellSourceSemanticValidator.Validate(document,
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, PowerShellCompilationCapabilities.HybridModule, framework));
        Assert.False(PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document));
        Assert.Contains(PowerShellSourceSemanticValidator.Validate(document,
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId), static diagnostic => diagnostic.Code == PowerShellCompilationFeatureIds.TypeDefinition);
    }

    [Theory]
    [InlineData("[object]$State", "")]
    [InlineData("AuthoredTransform() { throw 'Constructor must not execute during analysis.' }", "")]
    [InlineData("", "[Missing.Authored.Type]$Unknown = $null;")]
    [InlineData("", "[AuthoredTransform]::new();")]
    public void HostedAttributeClosureRejectsStateConstructorsAndUnclosedMemberTypes(string member, string body)
    {
        var source = Transform.Replace("return $Input", body + " return $Input").Replace("\n}", "\n" + member + "\n}") + "\n" + Consumer;
        Assert.False(PowerShellHostedAttributeDeclarationPolicy.IsQualified(Parse(source), "net10.0", PowerShellCompilationCapabilities.HybridModule));
    }

    [Theory]
    [InlineData("function Read-Value { param([AuthoredTransform()][string]$Value) $Value }")]
    [InlineData("function Read-Value { param([AuthoredTransform(1)][string]$Value) process { $Value } }")]
    [InlineData("function Read-Value { param([AuthoredTransform]$Value) process { $Value } }")]
    [InlineData("function Read-Value { param([AuthoredTransform()][string]$Value) process { [AuthoredTransform]::new() } }")]
    [InlineData("function Read-Value { param() process { function Read-Child { param([AuthoredTransform()][string]$Value) $Value }; Read-Child } }")]
    public void HostedAttributeClosureRejectsUnownedOrNonAttributeConsumers(string consumer)
        => Assert.False(PowerShellHostedAttributeDeclarationPolicy.IsQualified(Parse(Transform + "\n" + consumer), "net10.0", PowerShellCompilationCapabilities.HybridModule));

    [Fact]
    public void HostedAttributeClosureRejectsAmbiguousSuffixLookupAndConditionalDeclarations()
    {
        var ambiguous = Parse(Transform + "\n" + Transform.Replace("AuthoredTransform", "AuthoredTransformAttribute") + "\n" + Consumer);
        Assert.False(PowerShellHostedAttributeDeclarationPolicy.IsQualified(ambiguous, "net10.0", PowerShellCompilationCapabilities.HybridModule));
        var conditional = Parse("if ($true) { " + Transform + " }\n" + Consumer);
        Assert.False(PowerShellHostedAttributeDeclarationPolicy.IsQualified(conditional, "net10.0", PowerShellCompilationCapabilities.HybridModule));
    }

    [Fact]
    public void HostedAttributeClosureQualifiesStatelessValidationSignature()
    {
        var source = Transform.Replace("ArgumentTransformationAttribute", "ValidateArgumentsAttribute")
            .Replace("[object] Transform([System.Management.Automation.EngineIntrinsics]$Context, [object]$Input)", "[void] Validate([object]$Input, [System.Management.Automation.EngineIntrinsics]$Context)")
            .Replace("return $Input", "if ($null -eq $Input) { throw 'Missing value' }");
        Assert.True(PowerShellHostedAttributeDeclarationPolicy.IsQualified(Parse(source + "\n" + Consumer), "net10.0", PowerShellCompilationCapabilities.HybridModule));
    }

    private static ParsedSourceDocument Parse(string source)
        => PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "hosted-attribute-closure.psm1"));
}
