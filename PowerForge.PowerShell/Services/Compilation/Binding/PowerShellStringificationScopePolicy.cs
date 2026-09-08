namespace PowerForge;

/// <summary>Separates closed scalar conversion from native stringification that can execute caller-scope observers.</summary>
internal static class PowerShellStringificationScopePolicy
{
    internal static bool RequiresCallerScope(Type destination, PowerShellBoundExpression value)
        => HasStringElement(destination) && value.ValueState != PowerShellValueState.Null &&
           value.Type.ClrType != typeof(void) &&
           !PowerShellStableScalarTypePolicy.IsSupported(value.Type);

    private static bool HasStringElement(Type type)
        => type == typeof(string) || type.IsArray && HasStringElement(type.GetElementType()!);

    internal const string DiagnosticCode = "PSB2240";
    internal const string DiagnosticMessage =
        "Opaque string conversion can invoke callbacks that observe or mutate caller locals. " +
        "Collection stringification also observes the native OFS value. These operations remain on the PowerShell runtime path.";
}
