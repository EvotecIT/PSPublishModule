using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private const string StatementErrorContextType = "global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext";

    private void EmitNativeTry(StringBuilder builder, PowerShellLoweredTryStatement attempted, int indent,
        Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).AppendLine("try");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).AppendLine("    using (__statementErrors.EnterHandler())");
        EmitBlock(builder, attempted.Statements, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap);
        builder.Append(prefix).AppendLine("}");
        if (attempted.Catches.Length != 0)
        {
            var types = new List<string>();
            var indices = new List<int>();
            for (var index = 0; index < attempted.Catches.Length; index++)
            {
                var clause = attempted.Catches[index];
                if (clause.ExceptionTypes.Length == 0)
                {
                    types.Add("null");
                    indices.Add(index);
                }
                else
                    foreach (var type in clause.ExceptionTypes)
                    {
                        types.Add("typeof(" + PowerShellCSharpSymbolRenderer.TypeName(type) + ")");
                        indices.Add(index);
                    }
            }
            builder.Append(prefix).Append("catch (global::System.Exception ").Append(attempted.ExceptionTemporary)
                .Append(") when (").Append(StatementErrorContextType).Append(".IsOperationFailure(")
                .Append(attempted.ExceptionTemporary).AppendLine("))");
            builder.Append(prefix).AppendLine("{");
            builder.Append(prefix).Append("    var ").Append(attempted.ClauseTemporary).Append(" = __statementErrors.FindCatch(")
                .Append(attempted.ExceptionTemporary).Append(", new global::System.Type?[] { ").Append(string.Join(", ", types))
                .Append(" }, new int[] { ").Append(string.Join(", ", indices)).Append(" }, out var ")
                .Append(attempted.RecordTemporary).AppendLine(");");
            builder.Append(prefix).Append("    using (__statementErrors.EnterCatch(").Append(attempted.ExceptionTemporary).AppendLine("))");
            builder.Append(prefix).AppendLine("    {");
            for (var index = 0; index < attempted.Catches.Length; index++)
            {
                builder.Append(prefix).Append(index == 0 ? "    if (" : "    else if (")
                    .Append(attempted.ClauseTemporary).Append(" == ").Append(index).AppendLine(")");
                EmitBlock(builder, attempted.Catches[index].Statements, indent + 1, getTemporaryIdentifier, discardHelper, sourceMap);
            }
            builder.Append(prefix).AppendLine("    else { throw; }");
            builder.Append(prefix).AppendLine("    }");
            builder.Append(prefix).AppendLine("}");
        }
        if (attempted.FinallyStatements is not null)
        {
            builder.Append(prefix).AppendLine("finally");
            EmitBlock(builder, attempted.FinallyStatements.Value, indent, getTemporaryIdentifier, discardHelper, sourceMap);
        }
    }

    private void EmitStatementErrorBoundary(StringBuilder builder, PowerShellLoweredStatementErrorBoundary boundary,
        int indent, Func<string, string> getTemporaryIdentifier, string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).AppendLine("try");
        EmitBlock(builder, boundary.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
        builder.Append(prefix).Append("catch (global::System.Exception ").Append(boundary.ExceptionTemporary)
            .Append(") when (").Append(StatementErrorContextType).Append(".IsOperationFailure(")
            .Append(boundary.ExceptionTemporary).AppendLine("))");
        builder.Append(prefix).AppendLine("{");
        builder.Append(prefix).Append("    __statementErrors.Handle(").Append(boundary.ExceptionTemporary)
            .Append(", ").Append(PowerShellCSharpLiteral.QuoteString(boundary.SourcePath))
            .Append(", ").Append(boundary.Span.StartLine).Append(", ").Append(boundary.Span.StartColumn)
            .Append(", ").Append(boundary.Span.EndLine).Append(", ").Append(boundary.Span.EndColumn)
            .Append(", ").Append(PowerShellCSharpLiteral.QuoteString(boundary.SourceText)).AppendLine(");");
        builder.Append(prefix).AppendLine("}");
    }

    private string EmitProtectedClrInvocation(PowerShellLoweredClrInvocationExpression invocation)
    {
        var body = new StringBuilder();
        var receiver = invocation.Receiver is null ? string.Empty : invocation.ReceiverTemporary;
        if (invocation.Receiver is not null)
        {
            body.Append(invocation.ReceiverByReference ? "ref var " : "var ").Append(receiver)
                .Append(invocation.ReceiverByReference ? " = ref " : " = ").Append(EmitExpression(invocation.Receiver));
            if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.NormalizeNullString) body.Append(" ?? string.Empty");
            body.Append("; ");
        }
        if (invocation.RawArgumentTemporaries.Length != 0)
        {
            for (var index = 0; index < invocation.Arguments.Length; index++)
                body.Append(PowerShellCSharpSymbolRenderer.TypeName(invocation.Arguments[index].ClrType)).Append(' ')
                    .Append(invocation.RawArgumentTemporaries[index]).Append(" = ").Append(EmitExpression(invocation.Arguments[index])).Append("; ");
            if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellRuntimeException)
                body.Append("if (").Append(receiver).Append(" is null) throw __statementErrors.NullInvocation(); ");
            for (var index = 0; index < invocation.Arguments.Length; index++)
            {
                var conversion = invocation.ArgumentConversions[index];
                var value = conversion.Kind switch
                {
                    PowerShellClrArgumentConversionKind.None => invocation.RawArgumentTemporaries[index],
                    PowerShellClrArgumentConversionKind.Int32OrDoubleToInt32 =>
                        "__statementErrors.ConvertArgument<int>(" + invocation.RawArgumentTemporaries[index] + ", " +
                        PowerShellCSharpLiteral.QuoteString(conversion.ParameterName) + ", " + PowerShellCSharpLiteral.QuoteString(invocation.MemberName) + ")",
                    _ => throw new InvalidOperationException("Unsupported lowered CLR argument conversion.")
                };
                body.Append(PowerShellCSharpSymbolRenderer.TypeName(invocation.ParameterTypes[index])).Append(' ')
                    .Append(invocation.ArgumentTemporaries[index]).Append(" = ").Append(value).Append("; ");
            }
        }
        else
        {
            for (var index = 0; index < invocation.Arguments.Length; index++)
                body.Append(PowerShellCSharpSymbolRenderer.TypeName(invocation.ParameterTypes[index])).Append(' ')
                    .Append(invocation.ArgumentTemporaries[index]).Append(" = ").Append(EmitExpression(invocation.Arguments[index])).Append("; ");
            if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellRuntimeException)
                body.Append("if (").Append(receiver).Append(" is null) throw __statementErrors.NullInvocation(); ");
        }
        var arguments = string.Join(", ", invocation.ArgumentTemporaries);
        var operation = invocation.InvocationKind switch
        {
            PowerShellClrInvocationKind.Constructor => "new " + PowerShellCSharpSymbolRenderer.TypeName(invocation.DeclaringType) + "(" + arguments + ")",
            PowerShellClrInvocationKind.StaticMethod => PowerShellCSharpSymbolRenderer.TypeName(invocation.DeclaringType) + "." + invocation.MemberName + "(" + arguments + ")",
            _ => receiver + "." + invocation.MemberName + "(" + arguments + ")"
        };
        body.Append("try { ");
        if (invocation.ClrType != typeof(void)) body.Append("return ");
        body.Append(operation).Append("; } catch (global::System.Exception ").Append(invocation.ExceptionTemporary)
            .Append(") when (").Append(StatementErrorContextType).Append(".IsOperationFailure(")
            .Append(invocation.ExceptionTemporary).Append(")) { throw __statementErrors.WrapInvocation(")
            .Append(invocation.ExceptionTemporary).Append(", ")
            .Append(PowerShellCSharpLiteral.QuoteString(invocation.MemberName))
            .Append(", ").Append(invocation.Arguments.Length).Append("); }");
        var delegateType = invocation.ClrType == typeof(void) ? "global::System.Action" :
            "global::System.Func<" + PowerShellCSharpSymbolRenderer.TypeName(invocation.ClrType) + ">";
        return "new " + delegateType + "(() => { " + body + " })()";
    }
}
