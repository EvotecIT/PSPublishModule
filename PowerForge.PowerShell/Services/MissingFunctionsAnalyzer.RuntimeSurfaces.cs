using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PowerForge;

public sealed partial class MissingFunctionsAnalyzer
{
    private MissingFunctionsReport PreserveNonFunctionRuntimeSurfaces(
        MissingFunctionsReport report,
        HashSet<string> approved,
        IReadOnlyDictionary<string, ApprovedModuleSource> sources)
    {
        var retained = new HashSet<string>(report.NonInlineableApprovedModules, StringComparer.OrdinalIgnoreCase);
        foreach (string name in approved)
        {
            if (retained.Contains(name))
                continue;
            if (!sources.TryGetValue(name, out var source))
            {
                // Command resolution alone cannot prove a donor has no other runtime state.
                retained.Add(name);
                continue;
            }
            try
            {
                if (GetOrCreateModuleScopeSession(source).HasNonFunctionRuntimeSurface)
                    retained.Add(name);
            }
            catch
            {
                // Failure to inspect a selected donor is not evidence that it can be removed.
                retained.Add(name);
            }
        }
        return new MissingFunctionsReport(report.Summary, report.SummaryFiltered, report.Functions,
            report.FunctionsTopLevelOnly, report.AnalysisComplete,
            retained.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool HasNonFunctionRuntimeSurface(PSModuleInfo module)
        => EnumerateLoadedModuleGraph(module).Any(static current =>
            current.ModuleType != ModuleType.Script ||
            current.ExportedVariables.Count > 0 || current.ExportedAliases.Count > 0 ||
            current.ExportedCmdlets.Count > 0 || current.ExportedTypeFiles.Count > 0 ||
            current.ExportedFormatFiles.Count > 0);

    private static IEnumerable<PSModuleInfo> EnumerateLoadedModuleGraph(PSModuleInfo module)
    {
        // Ask the PowerShell owner for the loaded graph rather than interpreting declarations.
        // Required and nested modules can register runtime state that the parent does not export.
        var pending = new Stack<PSModuleInfo>();
        var visited = new HashSet<PSModuleInfo>();
        pending.Push(module);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;
            yield return current;
            foreach (var dependency in current.NestedModules.Concat(current.RequiredModules))
                pending.Push(dependency);
        }
    }
}
