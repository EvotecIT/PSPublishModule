using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns file-wide source contracts that must be preserved before bound-function analysis.</summary>
internal static class PowerShellSourceSemanticValidator
{
    /// <summary>
    /// Keeps an unqualified authored type on the module's hosted initialization path without
    /// preventing independent functions from using their ordinary binding and lowering rules.
    /// </summary>
    internal static bool CanRetainTypesBesideFunction(ParsedSourceDocument document,
        int functionStartOffset, int functionEndOffset, PowerShellCompilationCapability capabilities)
    {
        if (capabilities != PowerShellCompilationCapabilities.HybridModule || document.TypeClosure.Declarations.Length == 0 ||
            document.TypeClosure.Declarations.Any(static declaration => !declaration.IsDocumentScope)) return false;
        var consumingReferences = document.TypeClosure.References.Where(reference => reference.DeclarationCandidates.Length > 0 &&
            reference.ConsumerSpan.StartOffset >= functionStartOffset && reference.ConsumerSpan.EndOffset <= functionEndOffset).ToArray();
        if (consumingReferences.Length == 0) return true;
        var function = document.RootFunctionDeclarations.Select(static declaration => declaration.Syntax).FirstOrDefault(candidate =>
            candidate.Extent.StartOffset == functionStartOffset && candidate.Extent.EndOffset == functionEndOffset);
        if (function is null) return false;
        var names = PowerShellModuleTypeLoadPolicy.FindLexicalTypeAnchors(document, function);
        return consumingReferences.All(reference => reference.DeclarationCandidates.Length == 1 && names.Contains(reference.TypeName) &&
            reference.Kind is PowerShellSourceTypeReferenceKind.Constraint or PowerShellSourceTypeReferenceKind.Value);
    }

    /// <summary>Whether a function's metadata can be detached without recreating hosted type identities or imports.</summary>
    internal static bool SupportsDetachedFunctionMetadata(ParsedSourceDocument document)
        => document.TypeClosure.Declarations.Length == 0 &&
           !document.SyntaxRoot.FindAll(static node => node is UsingStatementAst { UsingStatementKind: not UsingStatementKind.Namespace },
               searchNestedScriptBlocks: false).Any();

    internal static PowerShellSemanticDiagnostic[] Validate(ParsedSourceDocument document, string semanticProfileId,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None, string? targetFramework = null)
    {
        var diagnostics = document.SyntaxRoot
            .FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
            .OfType<UsingStatementAst>()
            .Where(static statement => statement.UsingStatementKind != UsingStatementKind.Namespace)
            .Select(statement => new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.RuntimeUsing,
                $"Source '{statement.Extent.Text}' has runtime-bearing using semantics that cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                PowerShellSourceParser.GetSpan(document, statement.Extent)))
            .ToList();
        var requirementFailure = PowerShellScriptRequirementPolicy.GetFailure(document, semanticProfileId, capabilities);
        if (requirementFailure is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.RequiresDirective,
                requirementFailure,
                PowerShellSourceParser.GetSpan(document, document.SyntaxRoot.Extent)));
        }
        var typeDefinition = document.TypeClosure.Declarations.FirstOrDefault();
        if (typeDefinition is not null &&
            !PowerShellHostedAttributeDeclarationPolicy.IsQualified(document, targetFramework, capabilities) &&
            !PowerShellHostedEnumDeclarationPolicy.IsQualified(document, targetFramework, capabilities) &&
            !PowerShellHostedValueClassPolicy.IsQualified(document, targetFramework, capabilities))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.TypeDefinition,
                "PowerShell class and enum declarations define hosted runtime type identities; functions in this file remain on the PowerShell path until the canonical type-definition contract can lower them together.",
                typeDefinition.Span));
        }
        return diagnostics.ToArray();
    }
}
