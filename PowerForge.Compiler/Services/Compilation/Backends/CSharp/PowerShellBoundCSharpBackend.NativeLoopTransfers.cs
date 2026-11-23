using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private bool HasNativeLoopTransferOwner => _sourceFunction?.NativeFunctionBinding is not null;

    private void EmitNativeLoopTransferCatches(StringBuilder builder, string prefix, string label,
        Func<string, string> allocate)
    {
        var error = allocate("nativeLoopTransfer");
        builder.Append(prefix).Append("catch (global::System.Management.Automation.FlowControlException ").Append(error)
            .Append(") when (__nativeFunction.IsLoopTransfer(").Append(error).Append(", false, ")
            .Append(PowerShellCSharpLiteral.QuoteString(label)).AppendLine(")) { continue; }");
        builder.Append(prefix).Append("catch (global::System.Management.Automation.FlowControlException ").Append(error)
            .Append(") when (__nativeFunction.IsLoopTransfer(").Append(error).Append(", true, ")
            .Append(PowerShellCSharpLiteral.QuoteString(label)).AppendLine(")) { break; }");
    }
}
