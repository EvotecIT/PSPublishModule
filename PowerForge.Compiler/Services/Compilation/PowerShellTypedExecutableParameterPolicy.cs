namespace PowerForge;

/// <summary>Defines the CLR parameter types that the runtime-independent executable host can bind from process arguments.</summary>
internal static class PowerShellTypedExecutableParameterPolicy
{
    internal static void EnsureSupported(string parameterName, Type parameterType)
    {
        if (IsSupported(parameterType))
            return;

        throw new InvalidOperationException(
            $"Strict executable entry-point parameter '${parameterName}' has type '{parameterType.FullName}', " +
            "which cannot be bound from process arguments. Use a supported scalar or one-dimensional scalar array type.");
    }

    internal static void EnsureBindingSupported(
        IReadOnlyCollection<PowerShellTypedExecutableParameter> parameters,
        PowerShellCompilationCommandBinding commandBinding)
    {
        if (!string.IsNullOrWhiteSpace(commandBinding.DefaultParameterSetName) ||
            parameters.SelectMany(static parameter => parameter.Contract.Bindings)
                .Any(static binding => !string.IsNullOrWhiteSpace(binding.ParameterSetName)))
        {
            throw new InvalidOperationException(
                "Strict executable entry points with named parameter sets require PowerShell parameter-set selection and are not supported by the runtime-independent argument binder.");
        }

        var scalarRemaining = parameters.FirstOrDefault(parameter =>
            parameter.Contract.Bindings
                .Any(static binding => binding.ValueFromRemainingArguments) &&
            !parameter.ClrType.IsArray);
        if (scalarRemaining is not null)
        {
            throw new InvalidOperationException(
                $"Strict executable entry-point parameter '${scalarRemaining.Contract.Name}' uses ValueFromRemainingArguments with scalar type '{scalarRemaining.ClrType.FullName}', " +
                "whose PowerShell whitespace-joining semantics are not supported by the runtime-independent argument binder. Use a one-dimensional array type.");
        }
    }

    internal static bool IsSupported(Type type)
    {
        var compiledType = GetCompiledType(type);
        return PowerShellStableScalarTypePolicy.IsSupported(compiledType) ||
               compiledType.IsArray && compiledType.GetArrayRank() == 1 && PowerShellStableScalarTypePolicy.IsSupported(compiledType.GetElementType()!);
    }

    internal static Type GetCompiledType(Type type)
        => type == typeof(System.Management.Automation.SwitchParameter) ? typeof(bool) : type;

}
