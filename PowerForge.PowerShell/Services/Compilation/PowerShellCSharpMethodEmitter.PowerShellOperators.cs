using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private string EmitTypeTest(BinaryExpressionAst binary, bool negate)
    {
        if (binary.Right is not TypeExpressionAst typeExpression ||
            typeExpression.TypeName.GetReflectionType() is not { } targetType ||
            !PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, _targetFramework, _capabilities))
            throw Error(binary.Right, "The right operand of '-is' or '-isnot' must be a statically resolvable CLR type on the target surface.");

        if (Nullable.GetUnderlyingType(targetType) is not null)
        {
            return $"new global::System.Func<bool>(() => {{ _ = (object?)({EmitExpression(binary.Left)}); return {(negate ? "true" : "false")}; }})()";
        }

        var test = $"((object?)({EmitExpression(binary.Left)}) is {GetTypeName(targetType)})";
        return negate ? $"!{test}" : test;
    }

    private string EmitRegexMatch(BinaryExpressionAst binary, string operation)
    {
        if (_body.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("Matches", StringComparison.OrdinalIgnoreCase) &&
                               !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
                searchNestedScriptBlocks: false).Any())
        {
            throw Error(
                binary,
                "Regex matching whose $Matches automatic-variable state is observed requires PowerShell runtime semantics.");
        }
        EnsureScalarStrings(binary, operation);
        var options = operation.StartsWith("I", StringComparison.Ordinal)
            ? "global::System.Text.RegularExpressions.RegexOptions.IgnoreCase"
            : "global::System.Text.RegularExpressions.RegexOptions.None";
        var match = $"global::System.Text.RegularExpressions.Regex.IsMatch(({EmitExpression(binary.Left)} ?? string.Empty), ({EmitExpression(binary.Right)} ?? string.Empty), {options})";
        return operation.Contains("not", StringComparison.OrdinalIgnoreCase) ? $"!({match})" : match;
    }

    private string EmitRegexReplace(BinaryExpressionAst binary, string operation)
    {
        if (InferExpressionType(binary.Left) != typeof(string))
            throw Error(binary.Left, $"Operator '-{operation.ToLowerInvariant()}' currently requires a scalar String input.");

        string pattern;
        string replacement;
        if (InferExpressionType(binary.Right) == typeof(string))
        {
            pattern = EmitExpression(binary.Right);
            replacement = "string.Empty";
        }
        else if (binary.Right is ArrayLiteralAst { Elements: { Count: 2 } } arguments &&
                 arguments.Elements.All(element => InferExpressionType(element) == typeof(string)))
        {
            pattern = EmitExpression(arguments.Elements[0]);
            replacement = EmitExpression(arguments.Elements[1]);
        }
        else
        {
            throw Error(binary.Right, $"Operator '-{operation.ToLowerInvariant()}' requires a String pattern and optional String replacement.");
        }

        var options = operation == "Ireplace"
            ? "global::System.Text.RegularExpressions.RegexOptions.IgnoreCase"
            : "global::System.Text.RegularExpressions.RegexOptions.None";
        return $"global::System.Text.RegularExpressions.Regex.Replace(({EmitExpression(binary.Left)} ?? string.Empty), ({pattern} ?? string.Empty), ({replacement} ?? string.Empty), {options})";
    }

    private string EmitWildcardMatch(BinaryExpressionAst binary, string operation)
    {
        if (!_capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageOperators))
            throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' requires a PowerShell language-operator host capability.");
        EnsureScalarStrings(binary, operation);
        var options = operation.StartsWith("I", StringComparison.Ordinal)
            ? "global::System.Management.Automation.WildcardOptions.IgnoreCase"
            : "global::System.Management.Automation.WildcardOptions.None";
        var left = GetTemporaryIdentifier("wildcard_left");
        var right = GetTemporaryIdentifier("wildcard_right");
        var match = $"new global::System.Management.Automation.WildcardPattern(({right} ?? string.Empty), {options}).IsMatch(({left} ?? string.Empty))";
        if (operation.Contains("not", StringComparison.OrdinalIgnoreCase)) match = $"!({match})";
        return $"new global::System.Func<bool>(() => {{ var {left} = {EmitExpression(binary.Left)}; var {right} = {EmitExpression(binary.Right)}; return {match}; }})()";
    }

    private string EmitMembership(BinaryExpressionAst binary, string operation)
    {
        if (!_capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageOperators))
            throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' requires a PowerShell language-operator host capability.");

        var inOperator = operation.EndsWith("in", StringComparison.OrdinalIgnoreCase);
        var collection = inOperator ? binary.Right : binary.Left;
        var candidate = inOperator ? binary.Left : binary.Right;
        var collectionType = InferExpressionType(collection);
        if (!collectionType.IsArray || collectionType.GetArrayRank() != 1)
            throw Error(collection, $"Operator '-{operation.ToLowerInvariant()}' currently requires a statically typed one-dimensional array on its collection side.");

        var elementType = collectionType.GetElementType()!;
        var candidateType = InferExpressionType(candidate);
        if (candidateType != elementType && !CanAssign(elementType, candidateType))
            throw Error(candidate, $"Operator '-{operation.ToLowerInvariant()}' requires a candidate assignable to array element type '{elementType.FullName}'.");

        var ignoreCase = operation.StartsWith("I", StringComparison.Ordinal) ? "true" : "false";
        var left = GetTemporaryIdentifier("membership_left");
        var right = GetTemporaryIdentifier("membership_right");
        var item = GetTemporaryIdentifier("membership_item");
        var array = inOperator ? right : left;
        var value = inOperator ? left : right;
        var any = $"global::System.Linq.Enumerable.Any(({array} ?? global::System.Array.Empty<{GetTypeName(elementType)}>()), {item} => global::System.Management.Automation.LanguagePrimitives.Equals((object?){item}, (object?)({value}), {ignoreCase}, global::System.Globalization.CultureInfo.InvariantCulture))";
        if (operation.Contains("not", StringComparison.OrdinalIgnoreCase)) any = $"!({any})";
        return $"new global::System.Func<bool>(() => {{ var {left} = {EmitExpression(binary.Left)}; var {right} = {EmitExpression(binary.Right)}; return {any}; }})()";
    }

    private void EnsureScalarStrings(BinaryExpressionAst binary, string operation)
    {
        if (InferExpressionType(binary.Left) != typeof(string) || InferExpressionType(binary.Right) != typeof(string))
            throw Error(binary, $"Operator '-{operation.ToLowerInvariant()}' currently requires scalar String operands.");
    }
}
