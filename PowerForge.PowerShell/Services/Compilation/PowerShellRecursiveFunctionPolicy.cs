using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellRecursiveFunctionPolicy
{
    internal static bool TryGetDeclaredReturnType(
        FunctionDefinitionAst function,
        PowerShellCompilationUnitPlan unit,
        ISet<string> knownFunctionNames,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out Type? returnType)
    {
        returnType = null;
        var attributes = function.Body.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute =>
                attribute.TypeName.Name.Equals("OutputType", StringComparison.OrdinalIgnoreCase) ||
                attribute.TypeName.Name.Equals("OutputTypeAttribute", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? Array.Empty<AttributeAst>();
        if (attributes.Length != 1 ||
            attributes[0].NamedArguments.Count != 0 ||
            attributes[0].PositionalArguments.Count != 1 ||
            attributes[0].PositionalArguments[0] is not TypeExpressionAst typeExpression ||
            typeExpression.TypeName.GetReflectionType() is not { } declared ||
            declared == typeof(void) ||
            !PowerShellCompilationParameterTypePolicy.CanUseInMethod(declared, targetFramework, capabilities) ||
            unit.Parameters.Any(static parameter => parameter.DefaultValue is not null || parameter.Validations.Length > 0))
            return false;

        var commands = function.Body.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false)
            .OfType<CommandAst>()
            .ToArray();
        if (commands.Length == 0 || commands.Any(command =>
                command.InvocationOperator != TokenKind.Unknown ||
                command.GetCommandName() is not { } name ||
                !knownFunctionNames.Contains(name) ||
                !name.Equals(function.Name, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (function.Body.FindAll(
                static node => node is VariableExpressionAst variable && PowerShellBoundParametersPolicy.IsReference(variable),
                searchNestedScriptBlocks: false).Any())
            return false;

        returnType = declared;
        return true;
    }
}
