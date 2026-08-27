namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void HostedDictionariesPreserveHeterogeneousObjectValuesInIr()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapValue { $map = @{ Text = 'ready'; Count = 2; Enabled = $true }; return $map['Count'] } function Get-OrderedMapValue { $map = [ordered] @{ Text = 'ready'; Count = 2 }; return $map['Text'] }",
            TestPath("hosted-object-dictionaries.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var plain = Assert.IsType<PowerShellBoundDictionaryExpression>(
            Assert.IsType<PowerShellBoundAssignmentStatement>(Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-MapValue").Body.Statements[0]).Value);
        Assert.Equal(PowerShellBoundDictionaryKind.ObjectDictionary, plain.Kind);
        var ordered = Assert.IsType<PowerShellBoundDictionaryExpression>(
            Assert.IsType<PowerShellBoundAssignmentStatement>(Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-OrderedMapValue").Body.Statements[0]).Value);
        Assert.Equal(PowerShellBoundDictionaryKind.OrderedObjectDictionary, ordered.Kind);
        Assert.Contains("System.Collections.Hashtable", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_MapValue").Source, StringComparison.Ordinal);
        Assert.Contains("OrderedDictionary", Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Get_OrderedMapValue").Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeDictionariesStillRejectHeterogeneousValues()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapValue { $map = @{ Text = 'ready'; Count = 2 }; return $map['Count'] }",
            TestPath("runtime-free-object-dictionaries.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2702");
        Assert.Empty(result.Emitted.Methods);
    }

    [Fact]
    public void HostedDictionaryMemberAccessUsesThePowerShellAdapterContract()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapMember { param([System.Collections.IDictionary] $Map) return $Map.AjaxSessionKey }",
            TestPath("hosted-dictionary-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements)).Expression);
        Assert.Equal(PowerShellClrReceiverBehavior.PowerShellAdapter, member.ReceiverBehavior);
        Assert.Contains("Contains(\"AjaxSessionKey\")", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void HostedDictionaryMemberAssignmentUsesThePowerShellAdapterContract()
    {
        var document = PowerShellSourceParser.Parse(
            "function Set-MapMember { $Map = [ordered]@{}; $Map.Name = 42; return $Map.Name }",
            TestPath("hosted-dictionary-member-assignment.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var assignment = Assert.IsType<PowerShellBoundClrMemberAssignmentStatement>(
            Assert.Single(result.Analyzed.Functions).Body.Statements[1]);
        Assert.Equal(PowerShellClrReceiverBehavior.PowerShellAdapter, assignment.ReceiverBehavior);
        Assert.Contains(
            "((global::System.Collections.IDictionary)(Map))[\"Name\"] = 42",
            Assert.Single(result.Emitted.Methods).Source,
            StringComparison.Ordinal);
    }
}
