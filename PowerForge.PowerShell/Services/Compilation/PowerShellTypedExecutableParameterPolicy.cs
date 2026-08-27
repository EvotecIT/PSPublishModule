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

    internal static void EnsureBindingSupported(
        IReadOnlyCollection<ParameterAst> sourceParameters,
        IReadOnlyCollection<PowerShellCompilationParameter> parameters,
        PowerShellCompilationCommandBinding commandBinding)
    {
        if (!string.IsNullOrWhiteSpace(commandBinding.DefaultParameterSetName) ||
            parameters.SelectMany(static parameter => parameter.Bindings)
                .Any(static binding => !string.IsNullOrWhiteSpace(binding.ParameterSetName)))
        {
            throw new InvalidOperationException(
                "Strict executable entry points with named parameter sets require PowerShell parameter-set selection and are not supported by the runtime-independent argument binder.");
        }

        var parametersByName = parameters.ToDictionary(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var scalarRemaining = sourceParameters.FirstOrDefault(parameter =>
            parametersByName[parameter.Name.VariablePath.UserPath].Bindings
                .Any(static binding => binding.ValueFromRemainingArguments) &&
            !GetCompiledType(parameter.StaticType).IsArray);
        if (scalarRemaining is not null)
        {
            throw new InvalidOperationException(
                $"Strict executable entry-point parameter '${scalarRemaining.Name.VariablePath.UserPath}' uses ValueFromRemainingArguments with scalar type '{scalarRemaining.StaticType.FullName}', " +
                "whose PowerShell whitespace-joining semantics are not supported by the runtime-independent argument binder. Use a one-dimensional array type.");
        }
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
