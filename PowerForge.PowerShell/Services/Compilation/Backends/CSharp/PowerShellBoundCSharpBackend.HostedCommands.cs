using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitHostedBooleanCommand(PowerShellLoweredHostedBooleanCommandExpression command)
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

        return "global::System.Management.Automation.LanguagePrimitives.IsTrue(__invokePowerShellCapture(" +
               PowerShellCSharpLiteral.QuoteString(script.ToString()) + ", new object?[] { " +
               string.Join(", ", values.Select(static argument => EmitExpression(argument.Value!))) + " }))";
    }
}
