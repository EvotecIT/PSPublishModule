using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private int _outputCaptureDepth;

    /// <summary>Identifies native methods whose captured return must unwind the enclosing callback.</summary>
    private static bool TransfersCapturedReturn(PowerShellLoweredFunction function)
        => function.NativeFunctionBinding is not null &&
           PowerShellLoweredTreeEnumerator.EnumerateStatements(function.Statements)
               .OfType<PowerShellLoweredOutputCaptureStatement>()
               .Any(capture => PowerShellLoweredTreeEnumerator.EnumerateStatements(capture.Statements)
                   .Any(static statement => statement is PowerShellLoweredReturnStatement));

    /// <summary>Evaluates the return value into the active capture before leaving the whole native function.</summary>
    private void EmitCapturedReturn(StringBuilder builder, PowerShellLoweredReturnStatement returned, string prefix)
    {
        if (returned.Expression is not null)
        {
            if (returned.Expression.ClrType == typeof(void))
                builder.Append(prefix).Append(EmitExpression(returned.Expression)).AppendLine(";");
            else if (returned.EmitsValue)
                builder.Append(prefix).Append("__nativeFunction.WriteOutput(")
                    .Append(EmitExpression(returned.Expression)).AppendLine(", __writeOutput);");
            else
                builder.Append(prefix).Append("_ = ").Append(EmitExpression(returned.Expression)).AppendLine(";");
        }
        builder.Append(prefix).AppendLine("throw new global::PowerForge.Generated.Runtime.PowerShellCapturedReturnSignal();");
    }
}
