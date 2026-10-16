using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal static partial class PowerShellPackagedScriptRewriter
{
    /// <summary>Retained scripts use the same selected operand as native pipelines; unrelated values stay authored.</summary>
    private static SourceReplacement[] AddPathOperandGuards(ScriptBlockAst ast, SourceReplacement[] existing)
    {
        var guards = new List<SourceReplacement>();
        foreach (var command in ast.FindAll(static node => node is CommandAst, true).Cast<CommandAst>())
        {
            var operand = PowerShellPackagedPathOperandPolicy.Select(command);
            if (operand is null && PowerShellPackagedPathOperandPolicy.IsImplicitCurrentLocationRead(command))
            {
                if (existing.Any(replacement => replacement.StartOffset < command.Extent.EndOffset && replacement.EndOffset > command.Extent.StartOffset))
                    throw new InvalidOperationException("An implicit packaged provider admission overlaps another script-semantics rewrite.");
                guards.Add(new SourceReplacement(command.Extent.StartOffset, command.Extent.EndOffset,
                    "$([PowerForge.Generated.Runtime.PowerShellNativeFunctionFileSystemPathGuard]::ValidateCurrentLocation($ExecutionContext.SessionState); " + command.Extent.Text + ")"));
                continue;
            }
            if (operand is null || !PowerShellPackagedPathOperandPolicy.RequiresRuntimeGuard(operand)) continue;
            var extent = operand.Extent;
            // Nested guards cannot overlap: that would require evaluating a second source engine
            // or changing the selected pipeline's native evaluation semantics.
            if (guards.Any(guard => guard.StartOffset < extent.EndOffset && guard.EndOffset > extent.StartOffset))
                throw new InvalidOperationException("Nested packaged path operands require an unsupported overlapping guard.");
            var text = new StringBuilder(extent.Text);
            foreach (var replacement in existing.Where(replacement => replacement.StartOffset >= extent.StartOffset &&
                         replacement.EndOffset <= extent.EndOffset).OrderByDescending(static replacement => replacement.StartOffset))
            {
                text.Remove(replacement.StartOffset - extent.StartOffset, replacement.EndOffset - replacement.StartOffset);
                text.Insert(replacement.StartOffset - extent.StartOffset, replacement.Text);
            }
            if (existing.Any(replacement => replacement.StartOffset < extent.EndOffset && replacement.EndOffset > extent.StartOffset &&
                    (replacement.StartOffset < extent.StartOffset || replacement.EndOffset > extent.EndOffset)))
                throw new InvalidOperationException("A packaged provider-path guard overlaps another script-semantics rewrite.");
            guards.Add(new SourceReplacement(extent.StartOffset, extent.EndOffset,
                "([PowerForge.Generated.Runtime.PowerShellNativeFunctionFileSystemPathGuard]::Validate((" + text + "), $ExecutionContext.SessionState))"));
        }
        return existing.Where(replacement => !guards.Any(guard => replacement.StartOffset >= guard.StartOffset &&
                replacement.EndOffset <= guard.EndOffset))
            .Concat(guards).OrderByDescending(static replacement => replacement.StartOffset).ToArray();
    }
}
