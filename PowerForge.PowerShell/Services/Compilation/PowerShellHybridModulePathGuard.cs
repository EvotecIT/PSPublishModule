using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellHybridModulePathGuard
{
    internal static IEnumerable<PowerShellHybridSourceEdit> CreateEdits(ScriptBlockAst root)
    {
        foreach (var command in PowerShellCompilationSourcePathPlanner.GetModuleScopeDotSources(root))
        {
            if (!PowerShellCompilationSourcePathPlanner.TryPlanModuleAlternatives(root, command, out var operand, out var paths))
                continue;
            var alternatives = string.Join(", ", paths.Select(static path => "'" + path.Replace("'", "''") + "'"));
            var replacement = $$"""
                (& {
                    param($candidate, $root, $alternatives)
                    $separator = [System.IO.Path]::DirectorySeparatorChar
                    $comparison = [System.StringComparison]::Ordinal
                    if ($candidate -is [string]) {
                        if ($separator -eq [char]92) {
                            $candidate = $candidate.Replace([char]47, $separator)
                            $comparison = [System.StringComparison]::OrdinalIgnoreCase
                        }
                        foreach ($relative in $alternatives) {
                            $expected = [System.IO.Path]::Combine($root, $relative.Replace([char]47, $separator))
                            if ([string]::Equals($candidate, $expected, $comparison)) { return $expected }
                        }
                    }
                    throw 'Module dot-source path is outside its staged alternatives.'
                } {{operand!.Extent.Text}} $PSScriptRoot @({{alternatives}}))
                """;
            yield return new PowerShellHybridSourceEdit(
                operand!.Extent.StartOffset, operand.Extent.EndOffset - operand.Extent.StartOffset,
                replacement, "dot-source:" + operand.Extent.StartOffset);
        }
    }
}
