using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static bool RejectUnrepresentedTraps(ParsedSourceDocument document,
        IEnumerable<TrapStatementAst>? traps, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (traps?.FirstOrDefault() is not { } trap) return false;
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2946",
            "Trap statements require a separate error and continuation contract; their enclosing block remains native PowerShell.",
            PowerShellSourceParser.GetSpan(document, trap.Extent)));
        return true;
    }
}
