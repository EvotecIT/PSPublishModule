using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Resolves authored OutputType metadata once for binding and compatibility adapters.</summary>
internal static class PowerShellOutputTypeSemanticPolicy
{
    internal readonly record struct Contract(Type? SemanticType, string MetadataTypeName)
    {
        internal static Contract None => new(null, string.Empty);
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
        if (attributes.Length != 1)
        {
            errorNode = attributes[0];
            error = "OutputType metadata must declare exactly one attribute with one statically resolvable CLR type.";
            return false;
        }

        return TryResolve(attributes[0], targetFramework, capabilities, out contract, out errorNode, out error);
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
        if (attribute.NamedArguments.Count != 0 ||
            attribute.PositionalArguments.Count != 1 ||
            attribute.PositionalArguments[0] is not TypeExpressionAst typeExpression ||
            Resolve(typeExpression.TypeName) is not { } declared ||
            string.IsNullOrWhiteSpace(declared.FullName))
        {
            errorNode = attribute;
            error = "OutputType metadata must declare one statically resolvable CLR type.";
            return false;
        }

        var canUseAsSemanticContract = declared == typeof(void) ||
            PowerShellCompilationParameterTypePolicy.CanUseInMethod(declared, targetFramework, capabilities);
        if (!canUseAsSemanticContract &&
            !capabilities.HasFlag(PowerShellCompilationCapability.AdvisoryOutputTypeMetadata))
        {
            errorNode = attribute;
            error = $"OutputType metadata type '{declared.FullName}' cannot be represented by this target.";
            return false;
        }

        contract = new Contract(canUseAsSemanticContract ? declared : null, declared.FullName!);
        return true;
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
