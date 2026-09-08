namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitConversion(PowerShellLoweredConversionExpression conversion)
    {
        var value = EmitConversionValue(conversion);
        return conversion.NativeSourcePath is null ? value : EmitNativeExpressionPosition(
            value, conversion.Span, conversion.NativeSourcePath, conversion.NativeSourceText, conversion.NativePostTestCondition);
    }

    private string EmitConversionValue(PowerShellLoweredConversionExpression conversion)
    {
        var type = PowerShellCSharpSymbolRenderer.TypeName(conversion.ClrType);
        if (conversion.UseNativeConversion)
            return $"({type})__nativeFunction.ConvertValue(typeof({type}), {EmitExpression(conversion.Operand)})!";
        if (conversion.UsePowerShellTruthiness)
            return $"global::System.Management.Automation.LanguagePrimitives.IsTrue((object?)({EmitExpression(conversion.Operand)}))";
        if (conversion.NormalizeNullString)
            return $"({EmitExpression(conversion.Operand)} ?? string.Empty)";
        return conversion.UsePowerShellLanguageRuntime
            ? $"__powerForgeConvertInvariant<{type}>((object?)({EmitExpression(conversion.Operand)}))"
            : $"({type})({EmitExpression(conversion.Operand)})";
    }
}
