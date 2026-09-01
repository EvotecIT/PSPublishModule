using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns target-aware parameter and advanced-binding validation during semantic binding.</summary>
internal static class PowerShellParameterSemanticValidator
{
    internal static bool Validate(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IReadOnlyList<PowerShellCompilationParameter> parameters,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var paramBlock = function.Body.ParamBlock;
        if (paramBlock is null) return true;
        var valid = true;
        var executableEntryPoint = function.Name.StartsWith("__PowerForgeScript_", StringComparison.Ordinal) ||
                                   function.Name.Equals("Invoke", StringComparison.Ordinal) &&
                                   document.Path.EndsWith(".powerforge-entry.ps1", StringComparison.OrdinalIgnoreCase);

        foreach (var attribute in paramBlock.Attributes.OfType<AttributeAst>())
            valid &= ValidateMetadata(document, attribute, capabilities, targetFramework, diagnostics);

        for (var index = 0; index < paramBlock.Parameters.Count; index++)
        {
            var parameter = paramBlock.Parameters[index];
            var contract = parameters[index];
            var name = parameter.Name.VariablePath.UserPath;
            var span = PowerShellSourceParser.GetSpan(document, parameter.Extent);
            var hasAuthoredType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            if (!hasAuthoredType && !PowerShellCompilationParameterTypePolicy.CanUseUntypedObject(capabilities))
            {
                Add(diagnostics, PowerShellCompilationFeatureIds.ParameterType,
                    $"Parameter '${name}' is untyped; add an explicit type before compiling it to a CLR method.", span);
                valid = false;
            }
            else if (executableEntryPoint &&
                     capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                     !contract.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ProcessArgument))
            {
                Add(diagnostics, PowerShellCompilationFeatureIds.ParameterType,
                    $"Strict executable entry-point parameter '${name}' has type '{parameter.StaticType.FullName}', which cannot be bound from process arguments. Use a supported scalar or one-dimensional scalar array type.", span);
                valid = false;
            }

            if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticParameter(name, targetFramework))
            {
                Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                    $"Parameter '${name}' collides with a read-only automatic variable on target '{targetFramework ?? "the selected runtime"}'.", span);
                valid = false;
            }

            if (!executableEntryPoint &&
                capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
                contract.Bindings.Any(static binding => binding.ValueFromRemainingArguments))
            {
                Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                    $"Local function parameter '${name}' uses ValueFromRemainingArguments, whose command-line collection semantics are supported only on the strict executable entry point.", span);
                valid = false;
            }

            foreach (var attribute in parameter.Attributes.OfType<AttributeAst>())
                valid &= ValidateMetadata(document, attribute, capabilities, targetFramework, diagnostics);
        }

        valid &= ValidateBindingNames(document, paramBlock, targetFramework, diagnostics);
        valid &= ValidateBindingContract(document, paramBlock, parameters, diagnostics);
        return valid;
    }

    private static bool ValidateMetadata(
        ParsedSourceDocument document,
        AttributeAst attribute,
        PowerShellCompilationCapability capabilities,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (IsSupportedMetadata(attribute, capabilities, targetFramework)) return true;
        Add(diagnostics, PowerShellCompilationFeatureIds.ParameterMetadata,
            $"Parameter metadata syntax node 'AttributeAst' for '[{attribute.TypeName.Name}]' is not supported by this typed target.",
            PowerShellSourceParser.GetSpan(document, attribute.Extent));
        return false;
    }

    private static bool IsSupportedMetadata(AttributeAst attribute, PowerShellCompilationCapability capabilities, string? targetFramework)
    {
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "CmdletBinding"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument => IsSupportedCmdletBindingArgument(argument, capabilities));
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Parameter"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.All(argument => IsSupportedParameterArgument(argument, capabilities));
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Alias"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst { Value.Length: > 0 });
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "OutputType"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is TypeExpressionAst outputType &&
                   outputType.TypeName.GetReflectionType() is { } declared && declared != typeof(void) &&
                   PowerShellCompilationParameterTypePolicy.CanUseInMethod(declared, targetFramework, capabilities);
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowNull") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowEmptyString") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "AllowEmptyCollection") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateNotNull") ||
            PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateNotNullOrEmpty"))
            return attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "SupportsWildcards"))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateSet"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                   attribute.PositionalArguments.All(static argument => argument is StringConstantExpressionAst);
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidatePattern"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 1 &&
                   attribute.PositionalArguments[0] is StringConstantExpressionAst;
        if (PowerShellParameterContractBinder.IsAttributeNamed(attribute, "ValidateRange"))
            return attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count == 2 &&
                   attribute.PositionalArguments.All(static argument => PowerShellParameterContractBinder.TryGetInvariantNumericLiteral(argument, out _)) &&
                   attribute.Parent is ParameterAst parameter && IsNumericRangeType(parameter.StaticType);
        return false;
    }

    private static bool IsSupportedCmdletBindingArgument(NamedAttributeArgumentAst argument, PowerShellCompilationCapability capabilities)
    {
        if (argument.ArgumentName.Equals("PositionalBinding", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding)) return false;
        if (argument.ArgumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DefaultParameterSetName", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var setName) && !string.IsNullOrWhiteSpace(setName);
        return argument.ArgumentName.Equals("ConfirmImpact", StringComparison.OrdinalIgnoreCase) &&
               PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var impact) &&
               new[] { "None", "Low", "Medium", "High" }.Contains(impact, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSupportedParameterArgument(NamedAttributeArgumentAst argument, PowerShellCompilationCapability capabilities)
    {
        if (argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
            return PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ValueFromRemainingArguments", StringComparison.OrdinalIgnoreCase))
            return (capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) ||
                    capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding)) &&
                   PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("DontShow", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase) ||
            argument.ArgumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   PowerShellParameterContractBinder.TryGetBooleanAttributeValue(argument, out _);
        if (argument.ArgumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out var setName) && !string.IsNullOrWhiteSpace(setName);
        if (argument.ArgumentName.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase))
            return capabilities.HasFlag(PowerShellCompilationCapability.PipelineParameterBinding) &&
                   PowerShellParameterContractBinder.TryGetStringAttributeValue(argument, out _);
        return argument.ArgumentName.Equals("Position", StringComparison.OrdinalIgnoreCase) &&
               PowerShellParameterContractBinder.TryGetIntegerAttributeValue(argument, out var position) && position >= 0;
    }

    private static bool ValidateBindingNames(ParsedSourceDocument document, ParamBlockAst paramBlock, string? targetFramework, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var valid = true;
        var names = paramBlock.Parameters.ToDictionary(static parameter => parameter.Name.VariablePath.UserPath, static parameter => parameter.Name.VariablePath.UserPath, StringComparer.OrdinalIgnoreCase);
        var automatic = PowerShellCommonParameterPolicy.GetAvailable(paramBlock, targetFramework)
            .SelectMany(static parameter => new[] { parameter.Name, parameter.Alias })
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in paramBlock.Parameters)
        {
            var owner = parameter.Name.VariablePath.UserPath;
            foreach (var authored in new[] { owner }.Concat(PowerShellParameterContractBinder.GetAliases(parameter)))
            {
                if (automatic.Contains(authored))
                {
                    Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                        $"Parameter binding name '{authored}' is ambiguous because advanced commands add that common parameter or alias automatically.",
                        PowerShellSourceParser.GetSpan(document, parameter.Extent));
                    valid = false;
                }
                if (!authored.Equals(owner, StringComparison.OrdinalIgnoreCase) && names.TryGetValue(authored, out var existing) && !existing.Equals(owner, StringComparison.OrdinalIgnoreCase))
                {
                    Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                        $"Parameter alias '{authored}' is ambiguous between '${existing}' and '${owner}'.",
                        PowerShellSourceParser.GetSpan(document, parameter.Extent));
                    valid = false;
                }
                names[authored] = owner;
            }
        }
        return valid;
    }

    private static bool ValidateBindingContract(ParsedSourceDocument document, ParamBlockAst paramBlock, IReadOnlyList<PowerShellCompilationParameter> parameters, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var valid = true;
        var defaultSet = PowerShellAdvancedFunctionPolicy.GetBinding(paramBlock).DefaultParameterSetName;
        var namedSets = parameters.SelectMany(static parameter => parameter.Bindings).Select(static binding => binding.ParameterSetName)
            .Append(defaultSet).Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sets = namedSets.Length == 0 ? new[] { string.Empty } : namedSets;
        var effective = parameters.SelectMany(parameter => parameter.Bindings.SelectMany(binding =>
            string.IsNullOrWhiteSpace(binding.ParameterSetName)
                ? sets.Select(set => (parameter.Name, Set: set, Binding: binding))
                : new[] { (parameter.Name, Set: binding.ParameterSetName, Binding: binding) })).ToArray();
        var duplicateMembership = effective.GroupBy(static item => item.Name + "\0" + item.Set, StringComparer.OrdinalIgnoreCase).FirstOrDefault(static group => group.Count() > 1);
        if (duplicateMembership is not null)
        {
            var item = duplicateMembership.First();
            Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                $"Parameter '${item.Name}' declares duplicate metadata for effective parameter set '{DisplaySet(item.Set)}'.",
                PowerShellSourceParser.GetSpan(document, paramBlock.Extent));
            valid = false;
        }
        var duplicatePosition = effective.Where(static item => item.Binding.Position.HasValue)
            .GroupBy(static item => item.Set + "\0" + item.Binding.Position!.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicatePosition is not null)
        {
            var item = duplicatePosition.First();
            Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                $"More than one parameter is assigned position {item.Binding.Position} in parameter set '{DisplaySet(item.Set)}'.",
                PowerShellSourceParser.GetSpan(document, paramBlock.Extent));
            valid = false;
        }
        var duplicateRemaining = effective.Where(static item => item.Binding.ValueFromRemainingArguments)
            .GroupBy(static item => item.Set, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicateRemaining is not null)
        {
            Add(diagnostics, PowerShellCompilationFeatureIds.ParameterBinding,
                $"ValueFromRemainingArguments is assigned to more than one parameter in parameter set '{DisplaySet(duplicateRemaining.Key)}'.",
                PowerShellSourceParser.GetSpan(document, paramBlock.Extent));
            valid = false;
        }
        return valid;
    }

    private static bool IsNumericRangeType(Type type)
    {
        var valueType = type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()! : type;
        valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return valueType == typeof(byte) || valueType == typeof(sbyte) || valueType == typeof(short) || valueType == typeof(ushort) ||
               valueType == typeof(int) || valueType == typeof(uint) || valueType == typeof(long) || valueType == typeof(ulong) ||
               valueType == typeof(float) || valueType == typeof(double) || valueType == typeof(decimal);
    }

    private static string DisplaySet(string? name) => string.IsNullOrWhiteSpace(name) ? "__AllParameterSets" : name!;

    private static void Add(ICollection<PowerShellSemanticDiagnostic> diagnostics, string code, string message, SourceSpan span)
        => diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, span));
}
