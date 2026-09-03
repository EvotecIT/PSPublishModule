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
            foreach (var authoredName in new[] { parameterName }.Concat(PowerShellParameterContractBinder.GetAliases(parameter)))
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
        var duplicateMembership = effective
            .GroupBy(item => item.Name + "\0" + item.SetName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateMembership is not null)
        {
            var duplicate = duplicateMembership.First();
            var source = paramBlock.Parameters.First(candidate =>
                candidate.Name.VariablePath.UserPath.Equals(duplicate.Name, StringComparison.OrdinalIgnoreCase));
            diagnostics.Add(CreateDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Parameter '${duplicate.Name}' declares duplicate metadata for effective parameter set '{DisplayParameterSetName(duplicate.SetName)}'.",
                file,
                source.Extent,
                PowerShellCompilationFeatureIds.ParameterBinding));
        }
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

    private PowerShellCompilationParameter[] AnalyzeParameters(
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
            var parameterName = parameter.Name.VariablePath.UserPath;
            if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticParameter(parameterName, targetFramework))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameterName}' collides with a read-only automatic variable on target '{targetFramework ?? "the selected runtime"}'.",
                    file,
                    parameter.Extent,
                    PowerShellCompilationFeatureIds.ParameterBinding));
            }
            var type = parameter.StaticType;
            var hasExplicitType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            var isSwitch = type == typeof(System.Management.Automation.SwitchParameter);
            var typeCapabilities = hasExplicitType
                ? PowerShellCompilationParameterTypePolicy.Classify(isSwitch ? typeof(bool) : type, targetFramework)
                : PowerShellCompilationParameterTypePolicy.ClassifyUntyped(capabilities);
            if (!typeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedParameterType,
                    hasExplicitType
                        ? $"Parameter '${parameterName}' uses CLR type '{type.FullName}', which cannot be represented by the generated typed method surface."
                        : $"Parameter '${parameterName}' is untyped; add an explicit type before compiling it to a CLR method.",
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

            var bindings = PowerShellParameterContractBinder.GetBindings(parameter);
            if (!isScriptUnit &&
                capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                bindings.Any(static binding => binding.ValueFromRemainingArguments))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Local function parameter '${parameter.Name.VariablePath.UserPath}' uses ValueFromRemainingArguments, whose command-line collection semantics are supported only on the strict executable entry point.",
                    file,
                    parameter.Extent,
                    PowerShellCompilationFeatureIds.ParameterBinding));
            }
            if (parameter.DefaultValue is not null &&
                (!capabilities.HasFlag(PowerShellCompilationCapability.BoundParameters) ||
                 !PowerShellCompilationLiteralPolicy.TryResolve(
                     parameter.DefaultValue,
                     isSwitch ? typeof(bool) : type,
                     targetFramework,
                     _semanticProfileId,
                     out _)))
            {
                diagnostics.Add(CreateDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Parameter '${parameter.Name.VariablePath.UserPath}' declares a default value that cannot be preserved by this typed target.",
                    file,
                    parameter.DefaultValue.Extent,
                    PowerShellCompilationFeatureIds.ParameterDefault));
                AnalyzeNode(parameter.DefaultValue, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);
            }
            result.Add(PowerShellParameterContractBinder.Bind(parameter, targetFramework, capabilities, _semanticProfileId));

            foreach (var attribute in parameter.Attributes.Where(static attribute => attribute is not TypeConstraintAst))
                AnalyzeNode(attribute, unitRoot, file, diagnostics, localVariables, targetFramework, capabilities, localFunctionNames);
        }

        ValidateParameterBindingNames(paramBlock, targetFramework, file, diagnostics);
        ValidateParameterBindingContract(paramBlock, result, file, diagnostics);
        return result.ToArray();
    }

}
