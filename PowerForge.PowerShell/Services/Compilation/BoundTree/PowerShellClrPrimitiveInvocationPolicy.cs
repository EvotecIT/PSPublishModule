namespace PowerForge;

/// <summary>Shared classification of exact CLR primitive calls with no normal operation-failure route.</summary>
internal static class PowerShellClrPrimitiveInvocationPolicy
{
    internal static bool IsNonThrowing(Type declaringType, string memberName, PowerShellClrInvocationKind kind,
        Type returnType, IEnumerable<Type> parameterTypes)
        => kind == PowerShellClrInvocationKind.StaticMethod && declaringType == typeof(Math) &&
           memberName == nameof(Math.Sqrt) && returnType == typeof(double) &&
           parameterTypes.SequenceEqual(new[] { typeof(double) }) ||
           kind == PowerShellClrInvocationKind.StaticMethod && declaringType == typeof(object) &&
           memberName == nameof(object.ReferenceEquals) && returnType == typeof(bool) &&
           parameterTypes.SequenceEqual(new[] { typeof(object), typeof(object) }) ||
           kind == PowerShellClrInvocationKind.InstanceMethod && declaringType.IsEnum &&
           PowerShellClrTypeSemantics.IsIntegral(Enum.GetUnderlyingType(declaringType)) &&
           memberName == nameof(Enum.ToString) && returnType == typeof(string) && !parameterTypes.Any();
}
