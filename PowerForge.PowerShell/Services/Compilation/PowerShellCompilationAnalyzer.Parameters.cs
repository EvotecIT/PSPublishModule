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
                bindingNames.Add(name, name);
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

    private static PowerShellCompilationParameter[] AnalyzeParameters(
        ParamBlockAst? paramBlock,
        Ast unitRoot,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics,
        HashSet<string> localVariables,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames,
        bool isScriptUnit)
    {
        if (paramBlock is null)
            return Array.Empty<PowerShellCompilationParameter>();

        foreach (var attribute in paramBlock.Attributes)
            AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);

        var result = new List<PowerShellCompilationParameter>();
        foreach (var parameter in paramBlock.Parameters)
        {
            var type = parameter.StaticType;
            if (!IsSupportedParameterType(type))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' must declare a supported scalar or one-dimensional array type; resolved type was '{type.FullName}'.",
                    file,
                    parameter.Extent));
            }
            else if (isScriptUnit &&
                     capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                     !PowerShellTypedExecutableParameterPolicy.IsSupported(type))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    $"Strict executable entry-point parameter '${parameter.Name.VariablePath.UserPath}' has type '{type.FullName}', which cannot be bound from process arguments. Use a supported scalar or one-dimensional scalar array type.",
                    file,
                    parameter.Extent));
            }

            var isSwitch = type == typeof(System.Management.Automation.SwitchParameter);
            result.Add(new PowerShellCompilationParameter(
                parameter.Name.VariablePath.UserPath,
                (isSwitch ? typeof(bool) : type).FullName ?? type.Name,
                parameter.DefaultValue is not null,
                IsMandatoryParameter(parameter),
                isSwitch,
                GetAliases(parameter),
                HasMetadataAttribute(parameter, "AllowNull"),
                GetValidations(parameter)));

            foreach (var attribute in parameter.Attributes.Where(static attribute => attribute is not TypeConstraintAst))
                AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);
            if (parameter.DefaultValue is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' declares a PowerShell default value, which is not supported by the typed compiler.",
                    file,
                    parameter.DefaultValue.Extent));
                AnalyzeNode(parameter.DefaultValue, unitRoot, file, diagnostics, localVariables, capabilities, localFunctionNames);
            }
        }

        ValidateParameterBindingNames(paramBlock, file, diagnostics);
        return result.ToArray();
    }

    private static bool IsSupportedParameterType(Type type)
        => type == typeof(System.Management.Automation.SwitchParameter) ||
           SupportedParameterTypes.Contains(type) ||
           type.IsArray && type.GetArrayRank() == 1 && SupportedParameterTypes.Contains(type.GetElementType()!);
}
