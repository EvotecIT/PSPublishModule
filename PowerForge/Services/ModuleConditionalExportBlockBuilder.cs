using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class ModuleConditionalExportBlockBuilder
{
    internal static string BuildExportBlock(
        ExportSet exports,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies = null,
        string? moduleName = null)
    {
        var dependencies = NormalizeDependencies(conditionalFunctionDependencies);

        var builder = new StringBuilder(1024);
        builder.AppendLine("$FunctionsToExport = " + FormatPsStringList(exports.Functions));
        builder.AppendLine("$CmdletsToExport = " + FormatPsStringList(exports.Cmdlets));
        builder.AppendLine("$AliasesToExport = " + FormatPsStringList(exports.Aliases));

        if (dependencies.Count > 0)
        {
            builder.Append(BuildConditionalFilterBlock(dependencies, moduleName));
        }

        builder.AppendLine("Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport");
        return builder.ToString();
    }

    internal static string FormatPsStringList(IReadOnlyList<string>? values)
    {
        var list = (values ?? System.Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0)
            return "@()";

        var builder = new StringBuilder();
        builder.Append("@(");
        for (var i = 0; i < list.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append('\'').Append(EscapePsSingleQuoted(list[i])).Append('\'');
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static Dictionary<string, string[]> NormalizeDependencies(IReadOnlyDictionary<string, string[]>? dependencies)
    {
        var normalized = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (dependencies is null || dependencies.Count == 0)
            return normalized;

        foreach (var dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Key))
                continue;

            var commands = (dependency.Value ?? System.Array.Empty<string>())
                .Where(static command => !string.IsNullOrWhiteSpace(command))
                .Select(static command => command.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static command => command, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (commands.Length == 0)
                continue;

            normalized[dependency.Key.Trim()] = commands;
        }

        return normalized;
    }

    private static string BuildConditionalFilterBlock(
        IReadOnlyDictionary<string, string[]> dependencies,
        string? moduleName)
    {
        var moduleLabel = string.IsNullOrWhiteSpace(moduleName)
            ? "PowerForge"
            : moduleName!.Trim();

        var builder = new StringBuilder(1024);
        builder.AppendLine("$PowerForgeCommandModuleDependencies = @{");
        foreach (var dependency in dependencies.OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("    '")
                .Append(EscapePsSingleQuoted(dependency.Key))
                .Append("' = ")
                .AppendLine(FormatPsStringList(dependency.Value));
        }

        builder.AppendLine("}");
        builder.AppendLine("$PowerForgeFunctionsToRemove = [System.Collections.Generic.List[string]]::new()");
        builder.AppendLine("$PowerForgeAliasesToRemove = [System.Collections.Generic.List[string]]::new()");
        builder.AppendLine("$PowerForgeAllAliases = $null");
        builder.AppendLine("foreach ($PowerForgeDependencyModule in $PowerForgeCommandModuleDependencies.Keys) {");
        builder.AppendLine("    try {");
        builder.AppendLine("        Import-Module -Name $PowerForgeDependencyModule -ErrorAction Stop -Verbose:$false");
        builder.AppendLine("    } catch {");
        builder.AppendLine("        $PowerForgeDependencyCommands = @($PowerForgeCommandModuleDependencies[$PowerForgeDependencyModule])");
        builder.AppendLine("        if ($null -eq $PowerForgeAllAliases) {");
        builder.AppendLine("            $PowerForgeAllAliases = @(Get-Alias -ErrorAction SilentlyContinue)");
        builder.AppendLine("        }");
        builder.Append("        Write-Warning (\"[")
            .Append(EscapePsDoubleQuoted(moduleLabel))
            .AppendLine("] Optional dependency module '{0}' is not available. Commands not exported: {1}\" -f $PowerForgeDependencyModule, ($PowerForgeDependencyCommands -join ', '))");
        builder.AppendLine("        foreach ($PowerForgeFunction in $PowerForgeDependencyCommands) {");
        builder.AppendLine("            if ($PowerForgeFunction -and $PowerForgeFunction -notin $PowerForgeFunctionsToRemove) {");
        builder.AppendLine("                $PowerForgeFunctionsToRemove.Add($PowerForgeFunction)");
        builder.AppendLine("            }");
        builder.AppendLine("            $PowerForgeAllAliases | Where-Object { $_.Definition -eq $PowerForgeFunction } | ForEach-Object {");
        builder.AppendLine("                if ($_.Name -and $_.Name -notin $PowerForgeAliasesToRemove) {");
        builder.AppendLine("                    $PowerForgeAliasesToRemove.Add($_.Name)");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine("if ($PowerForgeFunctionsToRemove.Count -gt 0) {");
        builder.AppendLine("    $FunctionsToExport = @($FunctionsToExport | Where-Object { $_ -notin $PowerForgeFunctionsToRemove })");
        builder.AppendLine("    $AliasesToExport = @($AliasesToExport | Where-Object { $_ -notin $PowerForgeAliasesToRemove })");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EscapePsSingleQuoted(string value)
        => value?.Replace("'", "''") ?? string.Empty;

    private static string EscapePsDoubleQuoted(string value)
        => (value ?? string.Empty).Replace("`", "``").Replace("\"", "`\"");
}
