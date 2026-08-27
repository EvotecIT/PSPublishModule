namespace PowerForge;

internal sealed class PowerShellLocalFunctionSignature
{
    internal PowerShellLocalFunctionSignature(
        string sourceName,
        string generatedName,
        Type returnType,
        PowerShellLocalFunctionParameter[] parameters,
        bool isAdvancedFunction,
        bool requiresPowerShellBoundParameters = false,
        bool requiresPowerShellStreams = false,
        bool requiresPowerShellCommandRegions = false,
        PowerShellCompilationCommandBinding? commandBinding = null,
        bool requiresPowerShellRuntimeState = false,
        bool requiresPowerShellShouldProcess = false)
    {
        SourceName = sourceName;
        GeneratedName = generatedName;
        ReturnType = returnType;
        Parameters = parameters;
        IsAdvancedFunction = isAdvancedFunction;
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
        RequiresPowerShellShouldProcess = requiresPowerShellShouldProcess;
        CommandBinding = commandBinding ?? new PowerShellCompilationCommandBinding(isAdvancedFunction);
    }

    internal string SourceName { get; }
    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal PowerShellLocalFunctionParameter[] Parameters { get; }
    internal bool IsAdvancedFunction { get; }
    internal bool RequiresPowerShellBoundParameters { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal bool RequiresPowerShellRuntimeState { get; }
    internal bool RequiresPowerShellShouldProcess { get; }
    internal PowerShellCompilationCommandBinding CommandBinding { get; }
}

internal sealed class PowerShellLocalFunctionParameter
{
    internal PowerShellLocalFunctionParameter(
        string name,
        Type type,
        bool isMandatory,
        bool isSwitch,
        string[] aliases,
        bool allowNull,
        PowerShellCompilationValidation[] validations,
        PowerShellCompilationParameterBinding[]? bindings = null)
    {
        Name = name;
        Type = type;
        IsMandatory = isMandatory;
        IsSwitch = isSwitch;
        Aliases = aliases;
        AllowNull = allowNull;
        Validations = validations;
        Bindings = bindings ?? new[] { new PowerShellCompilationParameterBinding(mandatory: isMandatory) };
    }

    internal string Name { get; }
    internal Type Type { get; }
    internal bool IsMandatory { get; }
    internal bool IsSwitch { get; }
    internal string[] Aliases { get; }
    internal bool AllowNull { get; }
    internal PowerShellCompilationValidation[] Validations { get; }
    internal PowerShellCompilationParameterBinding[] Bindings { get; }
}
