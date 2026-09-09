using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns file-wide source contracts that must be preserved before bound-function analysis.</summary>
internal static class PowerShellSourceSemanticValidator
{
    /// <summary>Whether a function's metadata can be detached without recreating hosted type identities or imports.</summary>
    internal static bool SupportsDetachedFunctionMetadata(ParsedSourceDocument document)
        => !document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst ||
                node is UsingStatementAst { UsingStatementKind: not UsingStatementKind.Namespace }, searchNestedScriptBlocks: false).Any();

    internal static PowerShellSemanticDiagnostic[] Validate(ParsedSourceDocument document, string semanticProfileId)
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
        var requirementFailure = PowerShellScriptRequirementPolicy.GetFailure(document, semanticProfileId);
        if (requirementFailure is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.RequiresDirective,
                requirementFailure,
                PowerShellSourceParser.GetSpan(document, document.SyntaxRoot.Extent)));
        }
        var typeDefinition = document.SyntaxRoot
            .FindAll(static node => node is TypeDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<TypeDefinitionAst>()
            .FirstOrDefault();
        if (typeDefinition is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.TypeDefinition,
                "PowerShell class and enum declarations define hosted runtime type identities; functions in this file remain on the PowerShell path until the canonical type-definition contract can lower them together.",
                PowerShellSourceParser.GetSpan(document, typeDefinition.Extent)));
        }
        return diagnostics.ToArray();
    }
}
