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
                        parameter.Extent,
                        PowerShellCompilationFeatureIds.ParameterBinding));
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
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ISet<string>? localFunctionNames,
        bool isScriptUnit)
    {
        if (paramBlock is null)
            return Array.Empty<PowerShellCompilationParameter>();

        foreach (var attribute in paramBlock.Attributes)
            AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);

        var result = new List<PowerShellCompilationParameter>();
        foreach (var parameter in paramBlock.Parameters)
        {
            var type = parameter.StaticType;
            var hasExplicitType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            var isSwitch = type == typeof(System.Management.Automation.SwitchParameter);
            var typeCapabilities = hasExplicitType
                ? PowerShellCompilationParameterTypePolicy.Classify(isSwitch ? typeof(bool) : type, targetFramework)
                : PowerShellCompilationParameterTypeCapability.None;
            if (!typeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    hasExplicitType
                        ? $"Parameter '${parameter.Name.VariablePath.UserPath}' uses CLR type '{type.FullName}', which cannot be represented by the generated typed method surface."
                        : $"Parameter '${parameter.Name.VariablePath.UserPath}' is untyped; add an explicit type before compiling it to a CLR method.",
                    file,
                    parameter.Extent,
                    PowerShellCompilationFeatureIds.ParameterType));
            }
            else if (typeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost) &&
                     !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' uses PowerShell host type '{type.FullName}', which requires a binary-module host capability.",
                    file,
                    parameter.Extent,
                    PowerShellCompilationFeatureIds.ParameterType));
            }
            else if (isScriptUnit &&
                     capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                     !typeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ProcessArgument))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    $"Strict executable entry-point parameter '${parameter.Name.VariablePath.UserPath}' has type '{type.FullName}', which cannot be bound from process arguments. Use a supported scalar or one-dimensional scalar array type.",
                    file,
                    parameter.Extent,
                    PowerShellCompilationFeatureIds.ParameterType));
            }

            var bindings = GetParameterBindings(parameter);
            PowerShellCompilationLiteral? defaultValue = null;
            if (parameter.DefaultValue is not null &&
                (!capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters) ||
                 !PowerShellCompilationLiteralPolicy.TryResolve(
                     parameter.DefaultValue,
                     isSwitch ? typeof(bool) : type,
                     out defaultValue)))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' declares a default value that cannot be preserved by this typed target.",
                    file,
                    parameter.DefaultValue.Extent,
                    PowerShellCompilationFeatureIds.ParameterDefault));
                AnalyzeNode(parameter.DefaultValue, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);
            }
            result.Add(new PowerShellCompilationParameter(
                parameter.Name.VariablePath.UserPath,
                (isSwitch ? typeof(bool) : type).FullName ?? type.Name,
                parameter.DefaultValue is not null,
                bindings.Length > 0 && bindings.All(static binding => binding.Mandatory),
                isSwitch,
                GetAliases(parameter),
                HasMetadataAttribute(parameter, "AllowNull"),
                GetValidations(parameter),
                typeCapabilities,
                bindings,
                HasMetadataAttribute(parameter, "AllowEmptyString"),
                HasMetadataAttribute(parameter, "AllowEmptyCollection"),
                HasMetadataAttribute(parameter, "SupportsWildcards"),
                defaultValue));

            foreach (var attribute in parameter.Attributes.Where(static attribute => attribute is not TypeConstraintAst))
                AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);
        }

        ValidateParameterBindingNames(paramBlock, file, diagnostics);
        return result.ToArray();
    }

    private static PowerShellCompilationParameterBinding[] GetParameterBindings(ParameterAst parameter)
    {
        var attributes = parameter.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Parameter"))
            .ToArray();
        if (attributes.Length == 0)
            return new[] { new PowerShellCompilationParameterBinding() };

        return attributes.Select(attribute =>
        {
            var setName = GetNamedString(attribute, "ParameterSetName");
            if (setName.Equals("__AllParameterSets", StringComparison.OrdinalIgnoreCase))
                setName = string.Empty;
            return new PowerShellCompilationParameterBinding(
                setName,
                GetNamedBoolean(attribute, "Mandatory"),
                GetNamedInteger(attribute, "Position"),
                GetNamedBoolean(attribute, "ValueFromPipeline"),
                GetNamedBoolean(attribute, "ValueFromPipelineByPropertyName"),
                GetNamedBoolean(attribute, "ValueFromRemainingArguments"),
                GetNamedBoolean(attribute, "DontShow"),
                GetNamedString(attribute, "HelpMessage"));
        }).ToArray();
    }

    private static bool GetNamedBoolean(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate =>
            candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetBooleanAttributeValue(argument, out var value) && value;
    }

    private static int? GetNamedInteger(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate =>
            candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetIntegerAttributeValue(argument, out var value) ? value : null;
    }

    private static string GetNamedString(AttributeAst attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(candidate =>
            candidate.ArgumentName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return argument is not null && TryGetStringAttributeValue(argument, out var value) ? value : string.Empty;
    }
}
