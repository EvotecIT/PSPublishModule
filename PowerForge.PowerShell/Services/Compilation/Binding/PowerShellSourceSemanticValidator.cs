using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns file-wide source contracts that must be preserved before bound-function analysis.</summary>
internal static class PowerShellSourceSemanticValidator
{
    internal static PowerShellSemanticDiagnostic[] Validate(ParsedSourceDocument document)
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
        if (document.SyntaxRoot.ScriptRequirements is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.RequiresDirective,
                "Source #requires directives cannot be omitted from a typed artifact; this file must remain on the PowerShell runtime path.",
                PowerShellSourceParser.GetSpan(document, document.SyntaxRoot.Extent)));
        }
        return diagnostics.ToArray();
    }
}
