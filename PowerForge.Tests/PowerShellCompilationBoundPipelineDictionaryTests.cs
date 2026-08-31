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
    public void RuntimeFreeDictionariesUseBclObjectRepresentationForHeterogeneousValues()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapValue { $map = @{ Text = 'ready'; Count = 2 }; return $map['Count'] }",
            TestPath("runtime-free-object-dictionaries.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var dictionary = Assert.IsType<PowerShellBoundDictionaryExpression>(
            Assert.IsType<PowerShellBoundAssignmentStatement>(Assert.Single(result.Analyzed.Functions).Body.Statements[0]).Value);
        Assert.Equal(PowerShellBoundDictionaryKind.ObjectDictionary, dictionary.Kind);
        Assert.False(dictionary.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellHostTypes));
        Assert.Contains("System.Collections.Hashtable", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeKnownDictionaryMemberUsesKeyLookupBeforeClrMembers()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapMember { $map = [ordered] @{ Count = 42; Text = 'ready' }; return $map.Count }",
            TestPath("runtime-free-dictionary-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(result.Analyzed.Functions).Body.Statements[1]).Expression);
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback, member.ReceiverBehavior);
        Assert.Equal(typeof(object), member.Type.ClrType);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("Contains(\"Count\")", source, StringComparison.Ordinal);
        Assert.Contains("__pf_dictionary[\"Count\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(Map).Count", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeCompilerOwnedDictionaryReturnsNullForMissingMemberWithoutClrFallback()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapMember { $map = [ordered] @{ Name = 'ready' }; return $map.Missing }",
            TestPath("runtime-free-unknown-dictionary-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(result.Analyzed.Functions).Body.Statements[1]).Expression);
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookup, member.ReceiverBehavior);
        Assert.Contains("__pf_dictionary.Contains(\"Missing\") ?", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeOrderedObjectDictionaryIndexRemainsObjectValued()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapValue { $map = [ordered] @{ Count = 2; Text = 'ready' }; return $map['Count'] }",
            TestPath("runtime-free-ordered-object-dictionary-index.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var index = Assert.IsType<PowerShellBoundIndexExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(result.Analyzed.Functions).Body.Statements[1]).Expression);
        Assert.Equal(PowerShellBoundIndexKind.ObjectDictionary, index.Kind);
        Assert.Equal(typeof(object), index.Type.ClrType);
        Assert.DoesNotContain("(string?)", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeDictionaryMemberLookupPrefersAddedKeyBeforeClrFallback()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapMember { $map = [ordered] @{ Name = 1 }; $map['Count'] = 42; return $map.Count }",
            TestPath("runtime-free-mutated-dictionary-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(result.Analyzed.Functions).Body.Statements[2]).Expression);
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback, member.ReceiverBehavior);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("__pf_dictionary.Contains(\"Count\") ? __pf_dictionary[\"Count\"]", source, StringComparison.Ordinal);
        Assert.Contains("((global::System.Collections.Specialized.OrderedDictionary)__pf_dictionary).Count", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeDictionaryMemberLookupFallsBackAfterSameTypeReassignment()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapMember { $map = [ordered] @{ Count = 42 }; $map = [ordered] @{ Name = 1 }; return $map.Count }",
            TestPath("runtime-free-reassigned-dictionary-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("__pf_dictionary.Contains(\"Count\") ? __pf_dictionary[\"Count\"]", source, StringComparison.Ordinal);
        Assert.Contains("((global::System.Collections.Specialized.OrderedDictionary)__pf_dictionary).Count", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedDictionaryMemberAccessUsesTheKeyFirstContractInHostedBuilds()
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
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookup, member.ReceiverBehavior);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("__pf_dictionary.Contains(\"AjaxSessionKey\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PSObject.AsPSObject", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HostedDictionaryCountMemberUsesKeyFirstClrFallbackContract()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-MapCount { param([System.Collections.IDictionary] $Map) return $Map.Count }",
            TestPath("hosted-dictionary-count-member.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements)).Expression);
        Assert.Equal(PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback, member.ReceiverBehavior);
        Assert.False(member.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellHostTypes));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("__pf_dictionary.Contains(\"Count\") ? __pf_dictionary[\"Count\"]", source, StringComparison.Ordinal);
        Assert.Contains("((global::System.Collections.IDictionary)__pf_dictionary).Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(Map).Count", source, StringComparison.Ordinal);
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
