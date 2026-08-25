using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static void ValidateParameterBindingNames(
        ParamBlockAst paramBlock,
        string? targetFramework,
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
        var automaticBindingNames = PowerShellCommonParameterPolicy
            .GetAvailable(paramBlock, targetFramework)
            .SelectMany(static parameter => new[] { parameter.Name, parameter.Alias })
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in paramBlock.Parameters)
        {
            var parameterName = parameter.Name.VariablePath.UserPath;
            foreach (var authoredName in new[] { parameterName }.Concat(GetAliases(parameter)))
            {
                if (automaticBindingNames.Contains(authoredName))
                {
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Parameter binding name '{authoredName}' is ambiguous because advanced commands add that common parameter or alias automatically.",
                        file,
                        parameter.Extent,
                        PowerShellCompilationFeatureIds.ParameterBinding));
                }
                if (authoredName.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (bindingNames.TryGetValue(authoredName, out var owner) &&
                    !owner.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Parameter alias '{authoredName}' is ambiguous between '${owner}' and '${parameterName}'.",
                        file,
                        parameter.Extent,
                        PowerShellCompilationFeatureIds.ParameterBinding));
                    continue;
                }
                bindingNames[authoredName] = parameterName;
            }
        }
    }

    private static void ValidateParameterBindingContract(
        ParamBlockAst paramBlock,
        IReadOnlyList<PowerShellCompilationParameter> parameters,
        string file,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        foreach (var parameter in parameters)
        {
            var duplicateSet = parameter.Bindings
                .GroupBy(static binding => binding.ParameterSetName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateSet is null)
                continue;
            var source = paramBlock.Parameters.First(candidate =>
                candidate.Name.VariablePath.UserPath.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Parameter '${parameter.Name}' declares duplicate metadata for parameter set '{DisplayParameterSetName(duplicateSet.Key)}'.",
                file,
                source.Extent,
                PowerShellCompilationFeatureIds.ParameterBinding));
        }

        var commandBinding = PowerShellAdvancedFunctionPolicy.GetBinding(paramBlock);
        var namedSets = parameters
            .SelectMany(static parameter => parameter.Bindings)
            .Select(static binding => binding.ParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Append(commandBinding.DefaultParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (namedSets.Length > 32)
        {
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Command declares {namedSets.Length} parameter sets; PowerShell commands support at most 32.",
                file,
                paramBlock.Extent,
                PowerShellCompilationFeatureIds.ParameterBinding));
        }

        var sets = namedSets.Length == 0 ? new[] { string.Empty } : namedSets;
        var effective = parameters.SelectMany(parameter => parameter.Bindings.SelectMany(binding =>
            string.IsNullOrWhiteSpace(binding.ParameterSetName)
                ? sets.Select(setName => new { parameter.Name, SetName = setName, Binding = binding })
                : new[] { new { parameter.Name, SetName = binding.ParameterSetName, Binding = binding } })).ToArray();
        var duplicatePosition = effective
            .Where(static item => item.Binding.Position.HasValue)
            .GroupBy(item => item.SetName + "\0" + item.Binding.Position!.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicatePosition is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"More than one parameter is assigned position {duplicatePosition.First().Binding.Position} in parameter set '{DisplayParameterSetName(duplicatePosition.First().SetName)}'.",
                file,
                paramBlock.Extent,
                PowerShellCompilationFeatureIds.ParameterBinding));
        }
        var duplicateRemaining = effective
            .Where(static item => item.Binding.ValueFromRemainingArguments)
            .GroupBy(static item => item.SetName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicateRemaining is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"ValueFromRemainingArguments is assigned to more than one parameter in parameter set '{DisplayParameterSetName(duplicateRemaining.Key)}'.",
                file,
                paramBlock.Extent,
                PowerShellCompilationFeatureIds.ParameterBinding));
        }
    }

    private static string DisplayParameterSetName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "__AllParameterSets" : name!;

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

        ValidateParameterBindingNames(paramBlock, targetFramework, file, diagnostics);
        ValidateParameterBindingContract(paramBlock, result, file, diagnostics);
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
