using System.Collections;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Proves a closed String-value dictionary without changing its authored CLR identity.</summary>
internal static class PowerShellDictionaryShapePolicy
{
    internal static bool HasClosedStringValues(HashtableAst literal)
    {
        var assignment = FindContainingAssignment(literal);
        var variable = assignment is null ? null : PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
        var scope = FindScope(literal);
        if (variable is null || scope is null) return false;
        // A retained command can observe or mutate caller variables without receiving
        // the dictionary as an explicit argument. Such scopes need Object values.
        if (scope.Find(static node => node is CommandAst, false) is not null) return false;
        var assignments = scope.FindAll(static node => node is AssignmentStatementAst, false)
            .Cast<AssignmentStatementAst>().ToArray();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { variable.VariablePath.UserPath };
        bool changed;
        do
        {
            changed = false;
            foreach (var candidate in assignments)
            {
                var target = PowerShellAssignmentTargetPolicy.FindDirectVariable(candidate.Left);
                var source = FindReferenceVariable(candidate.Right);
                if (target is not null && source is not null && aliases.Contains(source.VariablePath.UserPath))
                    changed |= aliases.Add(target.VariablePath.UserPath);
            }
        } while (changed);

        var strings = new HashSet<string>(PowerShellParameterSyntax.GetParameters(scope)
            .Where(static parameter => parameter.StaticType == typeof(string))
            .Select(static parameter => parameter.Name.VariablePath.UserPath), StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in assignments)
            if (candidate.Left is ConvertExpressionAst { StaticType: var type } constrained && type == typeof(string) &&
                PowerShellAssignmentTargetPolicy.FindDirectVariable(constrained) is { } name)
                strings.Add(name.VariablePath.UserPath);

        foreach (var candidate in assignments)
        {
            var target = PowerShellAssignmentTargetPolicy.FindDirectVariable(candidate.Left);
            if (target is null || !aliases.Contains(target.VariablePath.UserPath)) continue;
            if (candidate.Operator != TokenKind.Equals) return false;
            var source = FindReferenceVariable(candidate.Right);
            if (source is not null && aliases.Contains(source.VariablePath.UserPath)) continue;
            var value = Unwrap(candidate.Right);
            if (value is ConvertExpressionAst conversion && PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion))
                value = conversion.Child;
            if (value is not HashtableAst table || table.KeyValuePairs.Any(pair => !IsString(pair.Item2, strings))) return false;
        }

        foreach (var reference in scope.FindAll(static node => node is VariableExpressionAst, false).Cast<VariableExpressionAst>())
        {
            if (!aliases.Contains(reference.VariablePath.UserPath)) continue;
            var outer = GetOuterReference(reference);
            if (outer.Parent is AssignmentStatementAst copy &&
                (ReferenceEquals(copy.Left, outer) ||
                 ReferenceEquals(copy.Right, outer) && PowerShellAssignmentTargetPolicy.FindDirectVariable(copy.Left) is { } target &&
                 aliases.Contains(target.VariablePath.UserPath))) continue;
            if (outer.Parent is IndexExpressionAst index && ReferenceEquals(index.Target, outer))
            {
                if (index.Parent is AssignmentStatementAst update && ReferenceEquals(update.Left, index))
                {
                    if (update.Operator != TokenKind.Equals || !IsString(update.Right, strings) || !IsString(index.Index, strings)) return false;
                }
                else if (index.Parent is UnaryExpressionAst || index.Parent is ConvertExpressionAst { StaticType: var conversionType } &&
                         conversionType == typeof(System.Management.Automation.PSReference)) return false;
                continue;
            }
            if (outer.Parent is InvokeMemberExpressionAst invocation && ReferenceEquals(invocation.Expression, outer) &&
                IsSafeInvocation(invocation, strings)) continue;
            if (outer.Parent is MemberExpressionAst member && member is not InvokeMemberExpressionAst &&
                ReferenceEquals(member.Expression, outer) && member.Member is StringConstantExpressionAst property)
            {
                if (member.Parent is AssignmentStatementAst update && ReferenceEquals(update.Left, member))
                {
                    if (update.Operator != TokenKind.Equals || !IsString(update.Right, strings)) return false;
                    continue;
                }
                if (member.Parent is UnaryExpressionAst || member.Parent is ConvertExpressionAst { StaticType: var memberConversion } &&
                    memberConversion == typeof(System.Management.Automation.PSReference)) return false;
                if (property.Value.ToLowerInvariant() is "count" or "isfixedsize" or "isreadonly" or "issynchronized") continue;
            }
            // Arguments, collection storage, foreach, mutable views, and unknown member
            // operations can expose aliases. Do not retain a narrowing cast after them.
            return false;
        }
        return true;
    }

    private static bool IsSafeInvocation(InvokeMemberExpressionAst invocation, ISet<string> strings)
    {
        if (invocation.Member is not StringConstantExpressionAst member) return false;
        var arguments = invocation.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        switch (member.Value.ToLowerInvariant())
        {
            case "add": return arguments.Length == 2 && arguments.All(argument => IsString(argument, strings));
            case "contains":
            case "containskey":
            case "containsvalue":
            case "remove": return arguments.Length == 1 && IsString(arguments[0], strings);
            case "clear":
            case "clone":
            case "gettype":
            case "gethashcode":
            case "tostring": return arguments.Length == 0;
            default: return false;
        }
    }

    private static bool IsString(Ast syntax, ISet<string> strings)
    {
        var value = Unwrap(syntax);
        return value is StringConstantExpressionAst or ExpandableStringExpressionAst ||
            value is ConvertExpressionAst { StaticType: var type } && type == typeof(string) ||
            value is VariableExpressionAst variable && strings.Contains(variable.VariablePath.UserPath) ||
            value is BinaryExpressionAst { Operator: TokenKind.Plus } addition &&
                IsString(addition.Left, strings) && IsString(addition.Right, strings);
    }

    private static ScriptBlockAst? FindScope(Ast syntax)
    {
        for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            if (parent is ScriptBlockAst block) return block;
        return null;
    }

    private static AssignmentStatementAst? FindContainingAssignment(Ast syntax)
    {
        var outer = GetOuterReference(syntax);
        return outer.Parent is AssignmentStatementAst assignment && ReferenceEquals(assignment.Right, outer) ? assignment : null;
    }

    private static Ast GetOuterReference(Ast syntax)
    {
        while (syntax.Parent is CommandExpressionAst or PipelineAst or ParenExpressionAst ||
               syntax.Parent is ConvertExpressionAst conversion &&
               (IsReferenceCast(conversion.StaticType) || PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion)))
            syntax = syntax.Parent;
        return syntax;
    }

    private static VariableExpressionAst? FindReferenceVariable(Ast syntax)
    {
        var value = Unwrap(syntax);
        while (value is ConvertExpressionAst conversion && IsReferenceCast(conversion.StaticType)) value = Unwrap(conversion.Child);
        return value as VariableExpressionAst;
    }

    private static bool IsReferenceCast(Type type) => type == typeof(object) || typeof(IDictionary).IsAssignableFrom(type);

    private static Ast Unwrap(Ast syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case PipelineAst { PipelineElements.Count: 1 } pipeline when pipeline.PipelineElements[0] is CommandExpressionAst expression:
                    syntax = expression.Expression; break;
                case CommandExpressionAst expression: syntax = expression.Expression; break;
                case ParenExpressionAst parenthesized: syntax = parenthesized.Pipeline; break;
                default: return syntax;
            }
        }
    }
}
