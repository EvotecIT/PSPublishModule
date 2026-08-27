using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Renders already-lowered CLR operations as deterministic readable C#.
/// </summary>
internal sealed class PowerShellBoundCSharpBackend
{
    internal PowerShellBoundCSharpResult Emit(PowerShellLoweredProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var methods = program.Functions.Select(function => EmitFunction(function, program.TargetCapabilities)).ToArray();
        return new PowerShellBoundCSharpResult(methods, program.Diagnostics);
    }

    private static PowerShellCSharpMethodEmission EmitFunction(
        PowerShellLoweredFunction function,
        PowerShellCompilationCapability targetCapabilities)
    {
        var builder = new StringBuilder();
        var parameterParts = function.Parameters.Select(parameter =>
            $"{PowerShellCSharpMethodEmitter.GetTypeName(parameter.ClrType)} {PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Symbol.Name)}").ToList();
        var requiresBoundParameters = function.Parameters.Any(parameter =>
            parameter.Contract.DefaultValue is not null ||
            !parameter.Contract.IsMandatory && parameter.Contract.Validations.Length > 0 &&
            targetCapabilities.HasFlag(PowerShellCompilationCapability.BoundParameters));
        if (requiresBoundParameters)
            parameterParts.Add("global::System.Collections.Generic.ISet<string> __boundParameters");
        var parameters = string.Join(", ", parameterParts);
        builder.Append("    public static ")
            .Append(PowerShellCSharpMethodEmitter.GetTypeName(function.ReturnType))
            .Append(' ')
            .Append(function.GeneratedName)
            .Append('(')
            .Append(parameters)
            .AppendLine(")")
            .AppendLine("    {")
            .AppendLine("        checked")
            .AppendLine("        {");

        var usedIdentifiers = function.Parameters.Select(static parameter => PowerShellClrSymbolMapper.MapIdentifier(parameter.Symbol.Name))
            .Concat(function.Locals.Select(static local => PowerShellClrSymbolMapper.MapIdentifier(local.Symbol.Name)))
            .ToHashSet(StringComparer.Ordinal);
        var temporaryIndex = 0;
        string GetTemporaryIdentifier(string prefix)
        {
            string candidate;
            do { candidate = $"__{prefix}_{temporaryIndex++}"; } while (!usedIdentifiers.Add(candidate));
            return candidate;
        }
        var parameterContracts = function.Parameters.Select(static parameter => new PowerShellParameterEmissionContract(
            parameter.Symbol.Name,
            parameter.ClrType,
            parameter.Contract)).ToArray();
        var prologue = new PowerShellParameterPrologueRenderer(
            targetCapabilities,
            PowerShellCSharpMethodEmitter.GetTypeName,
            GetTemporaryIdentifier).Render(parameterContracts);
        if (prologue.Length > 0)
        {
            foreach (var line in prologue.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                builder.Append("            ").AppendLine(line);
        }

        foreach (var statement in function.Statements) EmitStatement(builder, statement, 3, GetTemporaryIdentifier);

        builder.AppendLine("        }").Append("    }");
        return new PowerShellCSharpMethodEmission(
            function.GeneratedName,
            function.ReturnType,
            builder.ToString(),
            requiresPowerShellBoundParameters: requiresBoundParameters,
            help: function.Help?.ToPublicModel());
    }

    private static void EmitStatement(StringBuilder builder, PowerShellLoweredStatement statement, int indent, Func<string, string> getTemporaryIdentifier)
    {
        var prefix = new string(' ', indent * 4);
        switch (statement)
        {
            case PowerShellLoweredLocalDeclarationStatement declaration:
                builder.Append(prefix).Append(PowerShellCSharpMethodEmitter.GetTypeName(declaration.ClrType)).Append(' ')
                    .Append(PowerShellCSharpMethodEmitter.SanitizeIdentifier(declaration.Symbol.Name)).AppendLine(" = default!;");
                return;
            case PowerShellLoweredAssignmentStatement assignment:
                builder.Append(prefix);
                if (assignment.Declare) builder.Append(PowerShellCSharpMethodEmitter.GetTypeName(assignment.ClrType)).Append(' ');
                builder.Append(EmitMutation(
                    assignment.Target,
                    assignment.ClrType,
                    assignment.Operation,
                    assignment.Value,
                    assignment.NormalizeNullString,
                    assignment.CheckedIntegral)).AppendLine(";");
                return;
            case PowerShellLoweredIndexAssignmentStatement assignment:
                builder.Append(prefix).Append(EmitIndexAssignment(assignment)).AppendLine(";");
                return;
            case PowerShellLoweredReturnStatement { Expression: null }:
                builder.Append(prefix).AppendLine("return;");
                return;
            case PowerShellLoweredReturnStatement returned:
                builder.Append(prefix).Append("return ").Append(EmitExpression(returned.Expression!)).AppendLine(";");
                return;
            case PowerShellLoweredIfStatement conditional:
                for (var index = 0; index < conditional.Clauses.Length; index++)
                {
                    var clause = conditional.Clauses[index];
                    builder.Append(prefix).Append(index == 0 ? "if (" : "else if (").Append(EmitExpression(clause.Condition)).AppendLine(")");
                    EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier);
                }
                if (conditional.ElseStatements is not null)
                {
                    builder.Append(prefix).AppendLine("else");
                    EmitBlock(builder, conditional.ElseStatements, indent, getTemporaryIdentifier);
                }
                return;
            case PowerShellLoweredWhileStatement loop:
                builder.Append(prefix).Append("while (").Append(EmitExpression(loop.Condition)).AppendLine(")");
                EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier);
                return;
            case PowerShellLoweredForStatement loop:
                var initializer = loop.Initializer is null
                    ? string.Empty
                    : (loop.DeclareInitializer ? PowerShellCSharpMethodEmitter.GetTypeName(loop.Initializer.TargetClrType) + " " : string.Empty) + EmitExpression(loop.Initializer);
                var condition = loop.Condition is null ? "true" : EmitExpression(loop.Condition);
                var iterator = loop.Iterator is null ? string.Empty : EmitExpression(loop.Iterator);
                builder.Append(prefix).Append("for (").Append(initializer).Append("; ").Append(condition).Append("; ").Append(iterator).AppendLine(")");
                EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier);
                return;
            case PowerShellLoweredForEachStatement loop:
                var collection = EmitExpression(loop.Collection);
                var enumerable = loop.ScalarString
                    ? $"new[] {{ {collection} }}"
                    : $"({collection} ?? global::System.Array.Empty<{PowerShellCSharpMethodEmitter.GetTypeName(loop.ElementType)}>())";
                builder.Append(prefix).Append("foreach (")
                    .Append(PowerShellCSharpMethodEmitter.GetTypeName(loop.ElementType)).Append(' ')
                    .Append(PowerShellCSharpMethodEmitter.SanitizeIdentifier(loop.Variable.Name)).Append(" in ")
                    .Append(enumerable).AppendLine(")");
                EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier);
                return;
            case PowerShellLoweredSwitchStatement switchStatement:
                EmitSwitch(builder, switchStatement, indent, getTemporaryIdentifier);
                return;
            case PowerShellLoweredThrowStatement { Expression: null }:
                builder.Append(prefix).AppendLine("throw;");
                return;
            case PowerShellLoweredThrowStatement thrown:
                builder.Append(prefix).Append("throw ").Append(EmitExpression(thrown.Expression!)).AppendLine(";");
                return;
            case PowerShellLoweredTryStatement tryStatement:
                builder.Append(prefix).AppendLine("try");
                EmitBlock(builder, tryStatement.Statements, indent, getTemporaryIdentifier);
                foreach (var clause in tryStatement.Catches)
                {
                    if (clause.ExceptionTypes.Length == 0)
                    {
                        builder.Append(prefix).AppendLine("catch (global::System.Exception)");
                        EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier);
                        continue;
                    }
                    foreach (var exceptionType in clause.ExceptionTypes)
                    {
                        builder.Append(prefix).Append("catch (").Append(PowerShellCSharpMethodEmitter.GetTypeName(exceptionType)).AppendLine(")");
                        EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier);
                    }
                }
                if (tryStatement.FinallyStatements is not null)
                {
                    builder.Append(prefix).AppendLine("finally");
                    EmitBlock(builder, tryStatement.FinallyStatements, indent, getTemporaryIdentifier);
                }
                return;
            case PowerShellLoweredBreakStatement:
                builder.Append(prefix).AppendLine("break;");
                return;
            case PowerShellLoweredContinueStatement:
                builder.Append(prefix).AppendLine("continue;");
                return;
            default:
                throw new InvalidOperationException($"Lowered statement '{statement.GetType().Name}' has no C# rendering owner.");
        }
    }

    private static void EmitBlock(StringBuilder builder, IEnumerable<PowerShellLoweredStatement> statements, int indent, Func<string, string> getTemporaryIdentifier)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).AppendLine("{");
        foreach (var statement in statements) EmitStatement(builder, statement, indent + 1, getTemporaryIdentifier);
        builder.Append(prefix).AppendLine("}");
    }

    private static void EmitSwitch(
        StringBuilder builder,
        PowerShellLoweredSwitchStatement statement,
        int indent,
        Func<string, string> getTemporaryIdentifier)
    {
        var prefix = new string(' ', indent * 4);
        var valueIdentifier = getTemporaryIdentifier("switch_value");
        var matchedIdentifier = getTemporaryIdentifier("switch_matched");
        builder.Append(prefix).Append(PowerShellCSharpMethodEmitter.GetTypeName(statement.Value.ClrType)).Append(' ')
            .Append(valueIdentifier).Append(" = ").Append(EmitExpression(statement.Value)).AppendLine(";");
        builder.Append(prefix).Append("bool ").Append(matchedIdentifier).AppendLine(" = false;");
        builder.Append(prefix).AppendLine("do");
        builder.Append(prefix).AppendLine("{");
        foreach (var clause in statement.Clauses)
        {
            var clauseSource = EmitExpression(clause.Value);
            var comparison = statement.Value.ClrType == typeof(string)
                ? $"global::System.String.Equals({valueIdentifier}, {clauseSource}, global::System.StringComparison.{(statement.CaseSensitive ? "InvariantCulture" : "InvariantCultureIgnoreCase")})"
                : $"{valueIdentifier} == {clauseSource}";
            builder.Append(prefix).Append("    if (").Append(comparison).AppendLine(")");
            builder.Append(prefix).AppendLine("    {");
            builder.Append(prefix).Append("        ").Append(matchedIdentifier).AppendLine(" = true;");
            foreach (var nested in clause.Statements) EmitStatement(builder, nested, indent + 2, getTemporaryIdentifier);
            builder.Append(prefix).AppendLine("    }");
        }
        if (statement.DefaultStatements is not null)
        {
            builder.Append(prefix).Append("    if (!").Append(matchedIdentifier).AppendLine(")");
            EmitBlock(builder, statement.DefaultStatements, indent + 1, getTemporaryIdentifier);
        }
        builder.Append(prefix).AppendLine("}");
        builder.Append(prefix).AppendLine("while (false);");
    }

    private static string EmitExpression(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredLiteralExpression literal => EmitLiteral(literal),
            PowerShellLoweredVariableExpression variable => PowerShellCSharpMethodEmitter.SanitizeIdentifier(variable.Symbol.Name),
            PowerShellLoweredConversionExpression conversion =>
                $"({PowerShellCSharpMethodEmitter.GetTypeName(conversion.ClrType)})({EmitExpression(conversion.Operand)})",
            PowerShellLoweredBinaryExpression binary => EmitBinary(binary),
            PowerShellLoweredUnaryExpression unary => EmitUnary(unary),
            PowerShellLoweredMutationExpression mutation => EmitMutation(
                mutation.Target,
                mutation.TargetClrType,
                mutation.Operation,
                mutation.Value,
                mutation.NormalizeNullString,
                mutation.CheckedIntegral),
            PowerShellLoweredArrayExpression array => EmitArray(array),
            PowerShellLoweredDictionaryExpression dictionary => EmitDictionary(dictionary),
            PowerShellLoweredIndexExpression index => EmitIndex(index),
            PowerShellLoweredClrMemberExpression member => EmitClrMember(member),
            PowerShellLoweredClrInvocationExpression invocation => EmitClrInvocation(invocation),
            PowerShellLoweredInvocationExpression invocation =>
                $"{PowerShellCSharpMethodEmitter.SanitizeIdentifier(invocation.Target.Name)}({string.Join(", ", invocation.Arguments.Select(EmitExpression))})",
            _ => throw new InvalidOperationException($"Lowered expression '{expression.GetType().Name}' has no C# rendering owner.")
        };

    private static string EmitDictionary(PowerShellLoweredDictionaryExpression dictionary)
    {
        var entries = string.Join(", ", dictionary.Entries.Select(entry => $"{{ {EmitExpression(entry.Key)}, {EmitExpression(entry.Value)} }}"));
        return dictionary.Kind == PowerShellBoundDictionaryKind.OrderedStringDictionary
            ? $"new global::System.Collections.Specialized.OrderedDictionary(global::System.StringComparer.OrdinalIgnoreCase) {{ {entries} }}"
            : $"new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase) {{ {entries} }}";
    }

    private static string EmitIndex(PowerShellLoweredIndexExpression index)
    {
        var target = EmitExpression(index.Target);
        var key = EmitExpression(index.Index);
        if (index.Kind == PowerShellBoundIndexKind.StringDictionary)
            return $"({target} is null ? null : {target}.ContainsKey({key}) ? {target}[{key}] : null)";
        if (index.Kind is PowerShellBoundIndexKind.OrderedStringDictionary or PowerShellBoundIndexKind.ObjectDictionary)
            return $"({target} is null ? null : {target}.Contains({key}) ? {target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.String) target = $"({target} ?? string.Empty)";
        else target = $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({key}) < 0 ? {target}.Length + ({key}) : ({key}))";
        return $"({normalized} < 0 || {normalized} >= {target}.Length ? null : (object){target}[{normalized}])";
    }

    private static string EmitIndexAssignment(PowerShellLoweredIndexAssignmentStatement assignment)
    {
        var target = EmitExpression(assignment.Target);
        var index = EmitExpression(assignment.Index);
        var value = EmitExpression(assignment.Value);
        if (assignment.Kind != PowerShellBoundIndexKind.Array)
            return $"{target}[{index}] = {value}";
        var checkedTarget = $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({index}) < 0 ? {checkedTarget}.Length + ({index}) : ({index}))";
        var checkedIndex = $"({normalized} >= 0 && {normalized} < {checkedTarget}.Length ? {normalized} : throw new global::System.IndexOutOfRangeException(\"Index was outside the bounds of the array.\"))";
        return $"{checkedTarget}[{checkedIndex}] = {value}";
    }

    private static string EmitClrMember(PowerShellLoweredClrMemberExpression member)
    {
        if (member.IsStatic)
            return $"{PowerShellCSharpMethodEmitter.GetTypeName(member.DeclaringType)}.{member.MemberName}";
        if (member.Receiver is null) throw new InvalidOperationException("Instance CLR member has no lowered receiver.");
        var receiver = EmitExpression(member.Receiver);
        return member.ReceiverBehavior switch
        {
            PowerShellClrReceiverBehavior.NormalizeNullString => $"({receiver} ?? string.Empty).{member.MemberName}",
            PowerShellClrReceiverBehavior.NormalizeNullArrayLength =>
                $"({receiver} ?? global::System.Array.Empty<{PowerShellCSharpMethodEmitter.GetTypeName(member.DeclaringType.GetElementType()!)}>()).{member.MemberName}",
            PowerShellClrReceiverBehavior.PropagateNull => $"({receiver})?.{member.MemberName}",
            _ => $"({receiver}).{member.MemberName}"
        };
    }

    private static string EmitClrInvocation(PowerShellLoweredClrInvocationExpression invocation)
    {
        var arguments = string.Join(", ", invocation.Arguments.Select(EmitExpression));
        if (invocation.InvocationKind == PowerShellClrInvocationKind.Constructor)
            return $"new {PowerShellCSharpMethodEmitter.GetTypeName(invocation.DeclaringType)}({arguments})";
        if (invocation.InvocationKind == PowerShellClrInvocationKind.StaticMethod)
            return $"{PowerShellCSharpMethodEmitter.GetTypeName(invocation.DeclaringType)}.{invocation.MemberName}({arguments})";
        if (invocation.Receiver is null) throw new InvalidOperationException("Instance CLR invocation has no lowered receiver.");
        var receiver = EmitExpression(invocation.Receiver);
        if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.NormalizeNullString)
            receiver = $"({receiver} ?? string.Empty)";
        else
            receiver = $"({receiver})";
        return $"{receiver}.{invocation.MemberName}({arguments})";
    }

    private static string EmitArray(PowerShellLoweredArrayExpression array)
    {
        var elementType = array.ClrType.GetElementType()!;
        if (array.Elements.Length == 0) return $"global::System.Array.Empty<{PowerShellCSharpMethodEmitter.GetTypeName(elementType)}>()";
        return $"new {PowerShellCSharpMethodEmitter.GetTypeName(elementType)}[] {{ {string.Join(", ", array.Elements.Select(EmitExpression))} }}";
    }

    private static string EmitMutation(
        PowerShellSymbolId target,
        Type targetType,
        PowerShellBoundMutationOperator operation,
        PowerShellLoweredExpression? value,
        bool normalizeNullString,
        bool checkedIntegral)
    {
        var identifier = PowerShellCSharpMethodEmitter.SanitizeIdentifier(target.Name);
        if (operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement or
            PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement)
        {
            var incrementSymbol = operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.PostIncrement ? "+" : "-";
            return operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement
                ? (incrementSymbol == "+" ? "++" : "--") + identifier
                : identifier + (incrementSymbol == "+" ? "++" : "--");
        }
        if (value is null) throw new InvalidOperationException($"Mutation '{operation}' requires a value.");
        var right = EmitExpression(value);
        if (normalizeNullString) right = $"({right} ?? string.Empty)";
        if (operation == PowerShellBoundMutationOperator.Assign) return $"{identifier} = {right}";
        var symbol = operation switch
        {
            PowerShellBoundMutationOperator.Add => "+",
            PowerShellBoundMutationOperator.Subtract => "-",
            PowerShellBoundMutationOperator.Multiply => "*",
            PowerShellBoundMutationOperator.Divide => "/",
            PowerShellBoundMutationOperator.Remainder => "%",
            _ => throw new InvalidOperationException($"Mutation '{operation}' has no C# rendering owner.")
        };
        return checkedIntegral
            ? $"{identifier} = checked(({PowerShellCSharpMethodEmitter.GetTypeName(targetType)})({identifier} {symbol} {right}))"
            : $"{identifier} {symbol}= {right}";
    }

    private static string EmitBinary(PowerShellLoweredBinaryExpression expression)
    {
        var left = EmitExpression(expression.Left);
        var right = EmitExpression(expression.Right);
        if (expression.Operation is PowerShellBoundBinaryOperator.EqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualIgnoreCase or
            PowerShellBoundBinaryOperator.EqualCaseSensitive or PowerShellBoundBinaryOperator.NotEqualCaseSensitive)
        {
            var comparisonMode = expression.Operation is PowerShellBoundBinaryOperator.EqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualIgnoreCase
                ? "global::System.StringComparison.InvariantCultureIgnoreCase"
                : "global::System.StringComparison.InvariantCulture";
            var comparison = $"global::System.String.Equals({left}, {right}, {comparisonMode})";
            return expression.Operation is PowerShellBoundBinaryOperator.NotEqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualCaseSensitive
                ? $"!({comparison})"
                : comparison;
        }
        var symbol = expression.Operation switch
        {
            PowerShellBoundBinaryOperator.Add => "+",
            PowerShellBoundBinaryOperator.Subtract => "-",
            PowerShellBoundBinaryOperator.Multiply => "*",
            PowerShellBoundBinaryOperator.Divide => "/",
            PowerShellBoundBinaryOperator.Remainder => "%",
            PowerShellBoundBinaryOperator.Equal => "==",
            PowerShellBoundBinaryOperator.NotEqual => "!=",
            PowerShellBoundBinaryOperator.LessThan => "<",
            PowerShellBoundBinaryOperator.LessThanOrEqual => "<=",
            PowerShellBoundBinaryOperator.GreaterThan => ">",
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => ">=",
            PowerShellBoundBinaryOperator.LogicalAnd => "&&",
            PowerShellBoundBinaryOperator.LogicalOr => "||",
            PowerShellBoundBinaryOperator.BitwiseAnd => "&",
            PowerShellBoundBinaryOperator.BitwiseOr => "|",
            PowerShellBoundBinaryOperator.BitwiseExclusiveOr => "^",
            PowerShellBoundBinaryOperator.ShiftLeft => "<<",
            PowerShellBoundBinaryOperator.ShiftRight => ">>",
            _ => throw new InvalidOperationException($"Lowered binary operator '{expression.Operation}' has no C# rendering owner.")
        };
        if (expression.Operation is PowerShellBoundBinaryOperator.Divide or PowerShellBoundBinaryOperator.Remainder && expression.ClrType == typeof(double))
            return $"(((double)({left})) {symbol} ((double)({right})))";
        if (expression.Operation is PowerShellBoundBinaryOperator.ShiftLeft or PowerShellBoundBinaryOperator.ShiftRight)
            right = $"(int)({right})";
        return $"({left} {symbol} {right})";
    }

    private static string EmitUnary(PowerShellLoweredUnaryExpression expression)
    {
        var symbol = expression.Operation switch
        {
            PowerShellBoundUnaryOperator.Identity => "+",
            PowerShellBoundUnaryOperator.Negate => "-",
            PowerShellBoundUnaryOperator.LogicalNot => "!",
            PowerShellBoundUnaryOperator.BitwiseNot => "~",
            _ => throw new InvalidOperationException($"Lowered unary operator '{expression.Operation}' has no C# rendering owner.")
        };
        return $"({symbol}{EmitExpression(expression.Operand)})";
    }

    private static string EmitLiteral(PowerShellLoweredLiteralExpression literal)
    {
        if (literal.Value is null) return "null";
        var nullableType = Nullable.GetUnderlyingType(literal.ClrType);
        if (nullableType is not null)
        {
            var scalar = new PowerShellLoweredLiteralExpression(literal.Span, nullableType, literal.Value);
            return $"new {PowerShellCSharpMethodEmitter.GetTypeName(literal.ClrType)}({EmitLiteral(scalar)})";
        }
        if (literal.ClrType.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(literal.ClrType);
            var value = Type.GetTypeCode(underlying) is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                ? Convert.ToUInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "UL"
                : Convert.ToInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L";
            return $"({PowerShellCSharpMethodEmitter.GetTypeName(literal.ClrType)}){value}";
        }
        if (literal.Value is string text) return PowerShellCSharpLiteral.QuoteString(text);
        if (literal.Value is bool boolean) return boolean ? "true" : "false";
        if (literal.Value is char character) return $"'{character.ToString().Replace("'", "\\'")}'";
        if (literal.Value is float single) return single.ToString("R", CultureInfo.InvariantCulture) + "f";
        if (literal.Value is double doubleValue) return doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d";
        if (literal.Value is decimal decimalValue) return decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
        if (literal.Value is long longValue) return longValue.ToString(CultureInfo.InvariantCulture) + "L";
        if (literal.Value is ulong unsignedLong) return unsignedLong.ToString(CultureInfo.InvariantCulture) + "UL";
        if (literal.Value is uint unsignedInteger) return unsignedInteger.ToString(CultureInfo.InvariantCulture) + "U";
        if (literal.Value is Guid guid) return $"new global::System.Guid({PowerShellCSharpLiteral.QuoteString(guid.ToString("D"))})";
        if (literal.Value is DateTime dateTime)
            return $"new global::System.DateTime({dateTime.Ticks.ToString(CultureInfo.InvariantCulture)}L, (global::System.DateTimeKind){((int)dateTime.Kind).ToString(CultureInfo.InvariantCulture)})";
        if (literal.Value is DateTimeOffset dateTimeOffset)
            return $"new global::System.DateTimeOffset({dateTimeOffset.Ticks.ToString(CultureInfo.InvariantCulture)}L, new global::System.TimeSpan({dateTimeOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture)}L))";
        if (literal.Value is TimeSpan timeSpan) return $"new global::System.TimeSpan({timeSpan.Ticks.ToString(CultureInfo.InvariantCulture)}L)";
        if (literal.Value is Uri uri) return $"new global::System.Uri({PowerShellCSharpLiteral.QuoteString(uri.OriginalString)}, global::System.UriKind.RelativeOrAbsolute)";
        if (literal.Value is Version version) return $"new global::System.Version({PowerShellCSharpLiteral.QuoteString(version.ToString())})";
        if (literal.Value is System.Numerics.BigInteger bigInteger)
        {
            return $"global::System.Numerics.BigInteger.Parse({PowerShellCSharpLiteral.QuoteString(bigInteger.ToString(CultureInfo.InvariantCulture))}, global::System.Globalization.CultureInfo.InvariantCulture)";
        }
        return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "null";
    }
}

internal sealed class PowerShellBoundCSharpResult
{
    internal PowerShellBoundCSharpResult(PowerShellCSharpMethodEmission[] methods, PowerShellSemanticDiagnostic[] diagnostics)
    {
        Methods = methods;
        Diagnostics = diagnostics;
    }

    internal PowerShellCSharpMethodEmission[] Methods { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal bool Success => Methods.Length > 0 && Diagnostics.Length == 0;
}
