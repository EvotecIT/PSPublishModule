using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Resolves authored OutputType metadata once for binding and compatibility adapters.</summary>
internal static class PowerShellOutputTypeSemanticPolicy
{
    internal static bool TryResolve(
        ScriptBlockAst body,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out Type? outputType,
        out Ast? errorNode,
        out string? error)
    {
        outputType = null;
        errorNode = null;
        error = null;
        var attributes = body.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute =>
                attribute.TypeName.Name.Equals("OutputType", StringComparison.OrdinalIgnoreCase) ||
                attribute.TypeName.Name.Equals("OutputTypeAttribute", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? Array.Empty<AttributeAst>();
        if (attributes.Length == 0) return true;
        if (attributes.Length != 1 ||
            attributes[0].NamedArguments.Count != 0 ||
            attributes[0].PositionalArguments.Count != 1 ||
            attributes[0].PositionalArguments[0] is not TypeExpressionAst typeExpression ||
            typeExpression.TypeName.GetReflectionType() is not { } declared ||
            declared == typeof(void) ||
            !PowerShellCompilationParameterTypePolicy.CanUseInMethod(declared, targetFramework, capabilities))
        {
            errorNode = attributes[0];
            error = "OutputType metadata must declare one statically resolvable target-compatible CLR type.";
            return false;
        }

        outputType = declared;
        return true;
    }
}
