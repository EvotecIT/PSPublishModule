using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private string EmitPowerShellConversion(ConvertExpressionAst conversion)
    {
        var targetType = conversion.StaticType;
        if (PowerShellCompilationLiteralPolicy.TryResolve(conversion, targetType, out var literal) && literal is not null)
            return PowerShellCSharpLiteral.Emit(literal, targetType, GetTypeName);

        if (!_capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
            throw Error(conversion, $"Conversion to '{targetType.FullName}' requires a PowerShell language-conversion host capability.");

        var targetName = GetTypeName(targetType);
        var value = EmitExpression(conversion.Child);
        return $"({targetName})global::System.Management.Automation.LanguagePrimitives.ConvertTo((object?)({value}), typeof({targetName}), global::System.Globalization.CultureInfo.InvariantCulture)!";
    }
}
