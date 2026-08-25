using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Defines the CLR parameter types that the runtime-independent executable host can bind from process arguments.</summary>
internal static class PowerShellTypedExecutableParameterPolicy
{
    internal static void EnsureSupported(ParameterAst parameter)
    {
        if (IsSupported(parameter.StaticType))
            return;

        throw new InvalidOperationException(
            $"Strict executable entry-point parameter '${parameter.Name.VariablePath.UserPath}' has type '{parameter.StaticType.FullName}', " +
            "which cannot be bound from process arguments. Use a supported scalar or one-dimensional scalar array type.");
    }

    internal static bool IsSupported(Type type)
    {
        var compiledType = GetCompiledType(type);
        return IsScalar(compiledType) ||
               compiledType.IsArray && compiledType.GetArrayRank() == 1 && IsScalar(compiledType.GetElementType()!);
    }

    internal static Type GetCompiledType(Type type)
        => type == typeof(System.Management.Automation.SwitchParameter) ? typeof(bool) : type;

    private static bool IsScalar(Type type)
    {
        var scalar = Nullable.GetUnderlyingType(type) ?? type;
        return scalar.IsEnum ||
           scalar == typeof(bool) || scalar == typeof(byte) || scalar == typeof(sbyte) ||
           scalar == typeof(short) || scalar == typeof(ushort) || scalar == typeof(int) || scalar == typeof(uint) ||
           scalar == typeof(long) || scalar == typeof(ulong) || scalar == typeof(float) || scalar == typeof(double) ||
           scalar == typeof(decimal) || scalar == typeof(char) || scalar == typeof(string) ||
           scalar == typeof(DateTime) || scalar == typeof(DateTimeOffset) || scalar == typeof(TimeSpan) ||
           scalar == typeof(Guid) || scalar == typeof(Uri) || scalar == typeof(Version);
    }
}
