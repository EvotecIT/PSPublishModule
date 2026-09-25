using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Resolves authored OutputType metadata once for binding and compatibility adapters.</summary>
internal static class PowerShellOutputTypeSemanticPolicy
{
    internal readonly record struct Contract(
        Type? SemanticType,
        string MetadataTypeName,
        PowerShellOutputTypeDeclaration[] Declarations)
    {
        internal static Contract None => new(null, string.Empty, Array.Empty<PowerShellOutputTypeDeclaration>());
    }

    internal static bool TryResolve(
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out Contract contract,
        out Ast? errorNode,
        out string? error)
    {
        contract = Contract.None;
        errorNode = null;
        error = null;
        var attributes = body.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute =>
                attribute.TypeName.Name.Equals("OutputType", StringComparison.OrdinalIgnoreCase) ||
                attribute.TypeName.Name.Equals("OutputTypeAttribute", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? Array.Empty<AttributeAst>();
        if (attributes.Length == 0) return true;
        var declarations = new List<PowerShellOutputTypeDeclaration>();
        foreach (var attribute in attributes)
        {
            if (!TryResolve(attribute, targetFramework, capabilities, out var resolved, out errorNode, out error))
                return false;
            declarations.AddRange(resolved.Declarations);
            if (attributes.Length == 1)
                contract = resolved;
        }
        if (attributes.Length > 1)
        {
            if (!capabilities.HasFlag(PowerShellCompilationCapability.AdvisoryOutputTypeMetadata))
            {
                errorNode = attributes[0];
                error = "Multiple OutputType attributes require advisory metadata support on this target.";
                return false;
            }
            contract = new Contract(null, string.Empty, declarations.ToArray());
        }
        return true;
    }

    internal static bool TryResolve(
        AttributeAst attribute,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out Contract contract,
        out Ast? errorNode,
        out string? error)
    {
        contract = Contract.None;
        errorNode = null;
        error = null;
        string? parameterSetName = null;
        if (attribute.NamedArguments.Count == 1 &&
            attribute.NamedArguments[0].ArgumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase) &&
            attribute.NamedArguments[0].Argument is StringConstantExpressionAst setName &&
            !string.IsNullOrWhiteSpace(setName.Value))
            parameterSetName = setName.Value;
        else if (attribute.NamedArguments.Count != 0)
        {
            errorNode = attribute;
            error = "OutputType ParameterSetName must be one non-whitespace literal string.";
            return false;
        }
        if (attribute.PositionalArguments.Count == 0)
        {
            errorNode = attribute;
            error = "OutputType metadata requires at least one literal name or resolvable CLR type.";
            return false;
        }
        var strings = attribute.PositionalArguments.OfType<StringConstantExpressionAst>().ToArray();
        if (strings.Length == attribute.PositionalArguments.Count &&
            strings.All(static value => !string.IsNullOrWhiteSpace(value.Value)) &&
            capabilities.HasFlag(PowerShellCompilationCapability.AdvisoryOutputTypeMetadata))
        {
            var names = strings.Select(static value => value.Value).ToArray();
            contract = new Contract(null, names.Length == 1 && parameterSetName is null ? names[0] : string.Empty,
                new[] { new PowerShellOutputTypeDeclaration(names, parameterSetName, useClrTypes: false) });
            return true;
        }
        var types = attribute.PositionalArguments.OfType<TypeExpressionAst>().ToArray();
        if (types.Length == attribute.PositionalArguments.Count)
        {
            var resolved = types.Select(static type => Resolve(type.TypeName)).ToArray();
            if (resolved.All(static type => type is not null && !string.IsNullOrWhiteSpace(type.FullName)))
            {
                var compatible = resolved.All(type => type == typeof(void) ||
                    PowerShellCompilationParameterTypePolicy.CanUseInMethod(type!, targetFramework, capabilities));
                if (compatible || capabilities.HasFlag(PowerShellCompilationCapability.AdvisoryOutputTypeMetadata))
                {
                    var semantic = resolved.Length == 1 && parameterSetName is null && compatible ? resolved[0] : null;
                    if (semantic is not null || capabilities.HasFlag(PowerShellCompilationCapability.AdvisoryOutputTypeMetadata))
                    {
                        var names = resolved.Select(static type => type!.FullName!).ToArray();
                        contract = new Contract(semantic,
                            names.Length == 1 && parameterSetName is null ? names[0] : string.Empty,
                            new[] { new PowerShellOutputTypeDeclaration(names, parameterSetName, compatible) });
                        return true;
                    }
                }
            }
        }
        errorNode = attribute;
        error = "OutputType metadata requires literal names or resolvable CLR types representable on this target.";
        return false;
    }

    private static Type? Resolve(ITypeName typeName)
    {
        if (typeName.GetReflectionType() is { } resolved)
            return resolved;
        var fullName = typeName.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
            return null;
        resolved = Type.GetType(fullName, throwOnError: false, ignoreCase: true);
        if (resolved is not null)
            return resolved;
        return typeof(System.Management.Automation.PSObject).Assembly.GetType(
            fullName,
            throwOnError: false,
            ignoreCase: true);
    }
}
