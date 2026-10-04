namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static void AttachLocalCallEvidence(
        PowerShellCompilationFilePlan[] structural,
        PowerShellCompilationFilePlan[] analyzed,
        IReadOnlyList<SemanticUnitTarget> targets,
        PowerShellSemanticCompilationResult semantic)
    {
        var mapped = targets.Select(target =>
        {
            var fileIndex = Array.FindIndex(structural, file =>
                PowerShellCompilationPathSafety.PathEquals(file.FullPath, target.FilePath));
            var unitIndex = Array.IndexOf(structural[fileIndex].Units, target.Unit);
            return (Target: target, File: analyzed[fileIndex], Unit: analyzed[fileIndex].Units[unitIndex]);
        }).ToArray();
        foreach (var caller in mapped)
        {
            var edges = semantic.Analyzed.CallGraph.Where(edge =>
                edge.Caller.DocumentId == caller.Target.DocumentId &&
                edge.Caller.Declaration.StartOffset == caller.Target.DeclarationOffset &&
                edge.Caller.Name.Equals(caller.Target.SymbolName, StringComparison.OrdinalIgnoreCase));
            var calls = new List<PowerShellCompilationLocalCall>();
            foreach (var edge in edges)
            {
                var callee = mapped.FirstOrDefault(item =>
                    item.Target.DocumentId == edge.Callee.DocumentId &&
                    item.Target.DeclarationOffset == edge.Callee.Declaration.StartOffset &&
                    item.Target.SymbolName.Equals(edge.Callee.Name, StringComparison.OrdinalIgnoreCase));
                if (callee.Target is null) continue; // No invented link for an unresolved/synthetic-only symbol.
                var relativePath = PowerShellCompilationExplanationService.NormalizeRelativePath(
                    callee.File.RelativePath, Path.GetFileName(callee.File.FullPath));
                calls.Add(new PowerShellCompilationLocalCall
                {
                    CalleeUnitId = PowerShellCompilationExplanationService.ComputeUnitId(relativePath, callee.Unit),
                    CalleeName = callee.Unit.Name,
                    CalleeRelativePath = relativePath,
                    CalleeStartLine = callee.Unit.StartLine,
                    Line = caller.Target.Synthetic ? 0 : edge.Invocation.StartLine,
                    Column = caller.Target.Synthetic ? 0 : edge.Invocation.StartColumn
                });
            }
            caller.Unit.LocalCalls = calls.OrderBy(static call => call.Line).ThenBy(static call => call.Column)
                .ThenBy(static call => call.CalleeUnitId, StringComparer.Ordinal).ToArray();
        }
    }
}
