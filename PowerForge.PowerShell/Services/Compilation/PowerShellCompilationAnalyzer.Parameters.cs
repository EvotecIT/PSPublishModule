using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static void ValidateParameterBindingNames(
        ParamBlockAst paramBlock,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        var bindingNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in paramBlock.Parameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            if (!bindingNames.ContainsKey(name))
            {
                bindingNames.Add(name, name);
            }
        }
        foreach (var parameter in paramBlock.Parameters)
        {
            var parameterName = parameter.Name.VariablePath.UserPath;
            foreach (var alias in GetAliases(parameter))
            {
                if (bindingNames.TryGetValue(alias, out var owner) &&
                    !owner.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Parameter alias '{alias}' is ambiguous between '${owner}' and '${parameterName}'.",
                        file,
                        parameter.Extent));
                    continue;
                }
                bindingNames[alias] = parameterName;
            }
        }
    }
}
