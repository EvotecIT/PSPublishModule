using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellHostedEnumDeclarationTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void EnumParametersUseDeclaredNativeIdentityAndKeepStrictClosed(string framework)
    {
        var document = Parse("enum NativeChoice { Zero; One = 1 }; function Read-Choice { param([NativeChoice[]]$Value='One') $Value }");
        Assert.Empty(document.Errors);
        Assert.True(PowerShellHostedEnumDeclarationPolicy.IsQualified(document, framework, PowerShellCompilationCapabilities.HybridModule));
        Assert.Empty(PowerShellSourceSemanticValidator.Validate(document,
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, PowerShellCompilationCapabilities.HybridModule, framework));
        Assert.False(PowerShellHostedEnumDeclarationPolicy.IsQualified(document, framework, PowerShellCompilationCapability.None));
        Assert.False(PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document));
    }

    [Theory]
    [InlineData("enum NativeChoice { Zero }; function Read-Choice { param([NativeChoice]$Value) process {[NativeChoice]} }")]
    [InlineData("enum NativeChoice { Zero }; function Read-Choice { param() process { [NativeChoice]$Value='Zero'; $Value } }")]
    [InlineData("enum NativeChoice { Zero }; function Read-Choice { param([NativeChoice[][]]$Value) process {$Value} }")]
    [InlineData("enum NativeChoice { Zero }; function Read-Choice { param([NativeChoice[,]]$Value) process {$Value} }")]
    [InlineData("enum NativeChoice { Zero }; function Read-Choice { process {function Child {param([NativeChoice]$Value) $Value}} }")]
    [InlineData("if($true) { enum NativeChoice { Zero } }; function Read-Choice {param([NativeChoice]$Value) process {$Value}}")]
    [InlineData("enum NativeChoice : long { Zero }; function Read-Choice {param([NativeChoice]$Value) process {$Value}}")]
    public void EnumClosureRejectsUnclosedOrUnqualifiedConsumers(string source)
        => Assert.False(PowerShellHostedEnumDeclarationPolicy.IsQualified(Parse(source), "net10.0", PowerShellCompilationCapabilities.HybridModule));

    [Fact]
    public void EnumDeclarationDoesNotResolveUnrelatedUnknownParameterTypes()
    {
        var document = Parse("enum NativeChoice { Zero }; function Read-Choice { param([MissingChoice]$Value) process {$Value} }");
        var compiled = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(compiled.Lowered.Functions);
        Assert.Contains(compiled.Emitted.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationFeatureIds.ParameterType);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void EnumLiteralMembersAndFlagsKeepNativeValueAndDefaultOwnership(string framework)
    {
        var document = Parse("[Flags()] enum NativeChoice { Zero=0; One=1; Two=2 }; function Read-Choice {param([NativeChoice]$Value=[NativeChoice]::One) $Value.HasFlag([NativeChoice]::One)}; function Read-Flag { [NativeChoice]::Two }");
        Assert.Empty(document.Errors);
        Assert.True(PowerShellHostedEnumDeclarationPolicy.IsQualified(document, framework, PowerShellCompilationCapabilities.HybridModule));
        var compiled = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(2, compiled.Lowered.Functions.Count);
        Assert.False(PowerShellHostedEnumDeclarationPolicy.IsQualified(document, framework, PowerShellCompilationCapability.None));
    }

    [Theory]
    [InlineData("[NativeChoice]::Missing")]
    [InlineData("[NativeChoice]::$Name")]
    [InlineData("[NativeChoice]::new()")]
    [InlineData("[NativeChoice]::Parse('One')")]
    public void EnumValueAdmissionRejectsNonliteralAndUndeclaredMembers(string body)
        => Assert.False(PowerShellHostedEnumDeclarationPolicy.IsQualified(Parse("enum NativeChoice { One=1 }; function Read-Choice { " + body + " }"),
            "net10.0", PowerShellCompilationCapabilities.HybridModule));

    private static ParsedSourceDocument Parse(string source)
        => PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "hosted-enum-closure.psm1"));
}
