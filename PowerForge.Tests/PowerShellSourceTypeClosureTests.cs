using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellSourceTypeClosureTests
{
    [Fact]
    public void AuthoredMetadataAndNestedTypeArgumentsRetainTheirSourceDependencies()
    {
        const string source = """
            enum Mode { First; Second }
            class ResolveTypeAttribute : System.Management.Automation.ArgumentTransformationAttribute {
                [object] Transform([System.Management.Automation.EngineIntrinsics]$Context,[object]$InputData) { return $InputData }
            }
            function Read-Mode {
                param([ResolveType()][System.Collections.Generic.List[Mode[]]]$Value, [Missing.Authored.Type]$Other)
                [Mode]::First
            }
            """;
        var document = Parse(source);
        Assert.Empty(document.Errors);
        var mode = Assert.Single(document.TypeClosure.Declarations, static declaration => declaration.Name == "Mode");
        Assert.True(mode.IsEnum);
        Assert.True(mode.IsDocumentScope);
        Assert.Equal(document.DocumentId, mode.Span.DocumentId);
        Assert.Equal("enum Mode { First; Second }", source.Substring(mode.Span.StartOffset, mode.Span.EndOffset - mode.Span.StartOffset));
        var transform = Assert.Single(document.TypeClosure.Declarations, static declaration => !declaration.IsEnum);
        Assert.Equal("System.Management.Automation.ArgumentTransformationAttribute", Assert.Single(transform.BaseTypeNames));
        var attribute = Assert.Single(document.TypeClosure.References, static reference => reference.Kind == PowerShellSourceTypeReferenceKind.Attribute);
        Assert.Equal("Read-Mode", attribute.Consumer);
        Assert.Equal(transform.Span, Assert.Single(attribute.DeclarationCandidates));
        var references = document.TypeClosure.References.Where(static reference => reference.TypeName == "Mode").ToArray();
        Assert.Equal(2, references.Length);
        Assert.Contains(references, static reference => reference.Kind == PowerShellSourceTypeReferenceKind.Constraint);
        Assert.Contains(references, static reference => reference.Kind == PowerShellSourceTypeReferenceKind.Value);
        Assert.All(references, reference => Assert.Equal(mode.Span, Assert.Single(reference.DeclarationCandidates)));
        var missing = Assert.Single(document.TypeClosure.References, static reference => reference.TypeName == "Missing.Authored.Type");
        Assert.Empty(missing.DeclarationCandidates);
    }

    [Fact]
    public void FunctionHeaderParametersKeepAttributeAndNestedTypeDependencies()
    {
        var document = Parse("enum Mode { First }; class LocalAttribute {}; " +
            "function Read-Mode([Local()][System.Collections.Generic.List[Mode[]]]$Value) { $Value }");
        Assert.Empty(document.Errors);
        var mode = Assert.Single(document.TypeClosure.Declarations, static declaration => declaration.Name == "Mode");
        var modeReference = Assert.Single(document.TypeClosure.References, static reference => reference.TypeName == "Mode");
        Assert.Equal(PowerShellSourceTypeReferenceKind.Constraint, modeReference.Kind);
        Assert.Equal("Read-Mode", modeReference.Consumer);
        Assert.Equal(mode.Span, Assert.Single(modeReference.DeclarationCandidates));
        var attribute = Assert.Single(document.TypeClosure.References, static reference => reference.Kind == PowerShellSourceTypeReferenceKind.Attribute);
        Assert.Equal(Assert.Single(document.TypeClosure.Declarations, static declaration => declaration.Name == "LocalAttribute").Span,
            Assert.Single(attribute.DeclarationCandidates));
    }

    [Fact]
    public void AttributeNameAmbiguityKeepsEveryDeclarationCandidate()
    {
        var document = Parse("class Local {} ; class LocalAttribute {} ; function Read-Local { param([Local()]$Value) $Value }");
        Assert.Empty(document.Errors);
        var reference = Assert.Single(document.TypeClosure.References);
        Assert.Equal(2, reference.DeclarationCandidates.Length);
        Assert.Equal(2, reference.DeclarationCandidates.Distinct().Count());
    }

    [Fact]
    public void DeclarationFactsDoNotAuthorizeDetachingMetadataOrRemovingSourceGuard()
    {
        var document = Parse("enum Mode { First }; function Read-Mode { param([Mode]$Value) $Value }");
        Assert.False(PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(document));
        var diagnostic = Assert.Single(PowerShellSourceSemanticValidator.Validate(document, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId));
        Assert.Equal(PowerShellCompilationFeatureIds.TypeDefinition, diagnostic.Code);
        Assert.Equal(Assert.Single(document.TypeClosure.Declarations).Span, diagnostic.Span);
        var independent = Parse("function Read-Value { param([int]$Value) $Value }");
        Assert.Empty(independent.TypeClosure.Declarations);
        Assert.Empty(independent.TypeClosure.References);
        Assert.True(PowerShellSourceSemanticValidator.SupportsDetachedFunctionMetadata(independent));
    }

    private static ParsedSourceDocument Parse(string source)
        => PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "authored-types.ps1"));
}
