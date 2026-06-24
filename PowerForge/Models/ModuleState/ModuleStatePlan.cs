using System;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStatePlan
{
    internal ModuleStatePlan(
        ModuleStatePlanAction[]? actions,
        ModuleStateConflictFinding[]? findings)
    {
        Actions = actions ?? Array.Empty<ModuleStatePlanAction>();
        Findings = findings ?? Array.Empty<ModuleStateConflictFinding>();
    }

    internal ModuleStatePlanAction[] Actions { get; }

    internal ModuleStateConflictFinding[] Findings { get; }

    internal bool HasErrors => Findings.Any(static finding => finding.Severity == ModuleStateConflictSeverity.Error);
}
