using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Allows a later type use to keep the authored, invocation-local Add-Type guard in Hybrid.</summary>
/// <remarks>The C# declaration and its native imports stay with PowerShell; no generated CLR reference is inferred.</remarks>
internal static class PowerShellHostedGuardedAddTypePolicy
{
    internal static bool IsQualified(TypeExpressionAst typeExpression, PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
            return false;

        if (typeExpression.Parent is not MemberExpressionAst { Static: true } member ||
            !ReferenceEquals(member.Expression, typeExpression)) return false;

        // The guard names a runspace-created type, not an assembly-qualified dependency.
        // Keep the authored identity intact when the runtime resolver reparses the name.
        if (typeExpression.TypeName is not TypeName ||
            !string.IsNullOrEmpty(typeExpression.TypeName.AssemblyName)) return false;
        var name = typeExpression.TypeName.FullName;
        if (string.IsNullOrWhiteSpace(name) || !name.Contains('.')) return false;
        var function = FindOwningFunction(typeExpression);
        var statements = function?.Body?.EndBlock?.Statements;
        if (statements is null) return false;

        foreach (var statement in statements)
        {
            if (statement.Extent.EndOffset >= typeExpression.Extent.StartOffset) break;
            if (statement is not IfStatementAst guarded || guarded.Clauses.Count != 1 ||
                guarded.ElseClause is not null || !IsAbsentTypeGuard(guarded.Clauses[0].Item1, name))
                continue;
            var body = guarded.Clauses[0].Item2.Statements;
            if (body.Count == 1 && IsLiteralAddType(body[0])) return true;
        }
        return false;
    }

    private static FunctionDefinitionAst? FindOwningFunction(Ast syntax)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            if (parent is FunctionDefinitionAst function) return function;
        return null;
    }

    private static bool IsAbsentTypeGuard(PipelineBaseAst condition, string name)
    {
        if (condition is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst { Expression: UnaryExpressionAst unary } ||
            unary.TokenKind != TokenKind.Not || unary.Child is not ParenExpressionAst parenthesized ||
            parenthesized.Pipeline is not PipelineAst { PipelineElements.Count: 1 } inner ||
            inner.PipelineElements[0] is not CommandExpressionAst { Expression: BinaryExpressionAst binary } ||
            binary.Operator != TokenKind.As ||
            binary.Left is not StringConstantExpressionAst literal ||
            binary.Right is not TypeExpressionAst type ||
            !type.TypeName.FullName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
            !type.TypeName.FullName.Equals("System.Type", StringComparison.OrdinalIgnoreCase))
            return false;
        return literal.Value.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiteralAddType(StatementAst statement)
    {
        if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandAst command ||
            command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0 ||
            !string.Equals(command.GetCommandName(), "Add-Type", StringComparison.OrdinalIgnoreCase) ||
            command.CommandElements.Count != 3 ||
            command.CommandElements[1] is not CommandParameterAst parameter ||
            !parameter.ParameterName.Equals("TypeDefinition", StringComparison.OrdinalIgnoreCase) ||
            parameter.Argument is not null ||
            command.CommandElements[2] is not StringConstantExpressionAst { Value.Length: > 0 })
            return false;
        return true;
    }
}
