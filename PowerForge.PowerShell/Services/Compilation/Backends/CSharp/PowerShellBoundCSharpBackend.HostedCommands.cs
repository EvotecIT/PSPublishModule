using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private void EmitCommandRegion(StringBuilder builder, PowerShellLoweredCommandRegionStatement region, string prefix)
    {
        if (region.NativeSourcePath is not null)
        {
            builder.Append(prefix).Append("__nativeFunction.InvokeCommandRegion(")
                .Append(PowerShellCSharpLiteral.QuoteString(region.HostedFallbackSource)).Append(", ")
                .Append(PowerShellCSharpLiteral.QuoteString(region.NativeSourcePath)).Append(", ")
                .Append(region.Span.StartLine).Append(", ").Append(region.Span.StartColumn).Append(", ")
                .Append(PowerShellCSharpLiteral.QuoteString(region.NativeSourceDocument!)).Append(", ")
                .Append(region.Span.StartOffset).Append(", ").Append(region.Span.EndOffset).AppendLine(");");
            return;
        }
        builder.Append(prefix).Append("__invokePowerShellRegion(__statementErrors, ")
            .Append(PowerShellCSharpLiteral.QuoteString(region.HostedFallbackSource))
            .Append(", ").Append(EmitCommandRegionArguments(region.Arguments))
            .Append(", ").Append(EmitRegionSource(region.SourceSelection)).AppendLine(");");
    }

    private static string EmitRegionSource(PowerShellCommandRegionSourceSelection? selection)
        => selection is null ? "null" : "new global::PowerForge.Generated.Runtime.PowerShellHostedRegionSource(" +
            PowerShellCSharpLiteral.QuoteString(selection.Path) + ", " + PowerShellCSharpLiteral.QuoteString(selection.Document) +
            ", new int[] { " + string.Join(", ", selection.Statements.SelectMany(span => new[] { span.StartOffset, span.EndOffset })) + " })";

    private string EmitNativeCommandRecords(PowerShellLoweredNativeCommandExpression command, string sink)
        => $"__nativeFunction.InvokeCommandRegion({PowerShellCSharpLiteral.QuoteString(command.Source)}, " +
           $"{PowerShellCSharpLiteral.QuoteString(command.SourcePath)}, {command.Span.StartLine}, {command.Span.StartColumn}, " +
           $"{sink}, {PowerShellCSharpLiteral.QuoteString(command.SourceDocument)}, {command.Span.StartOffset}, {command.Span.EndOffset})";

    private string EmitNativeCommandCapture(PowerShellLoweredNativeCommandExpression command)
        => $"__nativeFunction.CaptureCommandRegion({PowerShellCSharpLiteral.QuoteString(command.Source)}, " +
           $"{PowerShellCSharpLiteral.QuoteString(command.SourcePath)}, {command.Span.StartLine}, {command.Span.StartColumn}, " +
           $"{(command.PreservePartialOutput ? "true" : "false")}" +
           (_nativeRegionPartialOutputSink is null ? "" : ", " + _nativeRegionPartialOutputSink) +
           $", {PowerShellCSharpLiteral.QuoteString(command.SourceDocument)}, {command.Span.StartOffset}, {command.Span.EndOffset})";

    private string EmitHostedBooleanCommand(PowerShellLoweredHostedBooleanCommandExpression command)
    {
        var module = command.Provider.ModuleNames.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(module))
            throw new InvalidOperationException($"Hosted Boolean provider '{command.Provider.ProviderId}' requires one canonical module name.");
        var script = new StringBuilder("param(");
        var values = command.Arguments.Where(static argument => argument.Value is not null).ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0) script.Append(", ");
            script.Append("$__pfArg").Append(index);
        }
        script.Append(") [bool](")
            .Append(module).Append('\\').Append(command.Provider.CommandName);
        var valueIndex = 0;
        foreach (var argument in command.Arguments)
        {
            script.Append(" -").Append(argument.ParameterName);
            if (argument.Value is not null)
                script.Append(" $__pfArg").Append(valueIndex++);
        }
        script.Append(')');

        return "global::System.Management.Automation.LanguagePrimitives.IsTrue(__invokePowerShellCapture(__statementErrors, " +
               PowerShellCSharpLiteral.QuoteString(script.ToString()) + ", new object?[] { " +
               string.Join(", ", values.Select(argument => EmitExpression(argument.Value!))) + " }, null))";
    }
}
