namespace PowerForge;

/// <summary>
/// Models the bounded PowerShell conversion applied when a null collection value
/// enters a by-value pipeline parameter. A null collection is one null pipeline
/// record; it is not the same as an empty collection.
/// </summary>
internal static class PowerShellPipelineNullInputSemanticPolicy
{
    internal static bool TryBindElement(Type elementType, SourceSpan span, out PowerShellBoundExpression element)
    {
        object? value;
        if (Nullable.GetUnderlyingType(elementType) is not null ||
            elementType == typeof(Uri) ||
            elementType == typeof(Version))
        {
            value = null;
        }
        else if (elementType == typeof(string))
        {
            value = string.Empty;
        }
        else if (PowerShellClrTypeSemantics.IsNumeric(elementType) || elementType == typeof(char))
        {
            value = Activator.CreateInstance(elementType);
        }
        else
        {
            element = null!;
            return false;
        }

        element = new PowerShellBoundLiteralExpression(
            span,
            value,
            new PowerShellTypeFact(
                elementType,
                PowerShellTypeFactProvenance.Inferred,
                "PowerShell by-value pipeline binding converts one null input record to this stable scalar value."),
            value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
        return true;
    }
}
