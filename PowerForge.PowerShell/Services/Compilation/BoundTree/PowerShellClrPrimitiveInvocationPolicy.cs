namespace PowerForge;

/// <summary>Shared classification of exact CLR primitive calls with no normal operation-failure route.</summary>
internal static class PowerShellClrPrimitiveInvocationPolicy
{
    internal static bool IsNonThrowing(Type declaringType, string memberName, PowerShellClrInvocationKind kind,
        Type returnType, IEnumerable<Type> parameterTypes)
        => kind == PowerShellClrInvocationKind.StaticMethod && declaringType == typeof(Math) &&
           memberName == nameof(Math.Sqrt) && returnType == typeof(double) &&
           parameterTypes.SequenceEqual(new[] { typeof(double) });
}
