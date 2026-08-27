using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Neutral parameter contract consumed by lowering and emission after SMA binding.</summary>
internal sealed class PowerShellBoundParameterContract
{
    private PowerShellBoundParameterContract(string name, Type clrType, PowerShellCompilationParameter metadata)
    {
        Name = name;
        ClrType = clrType;
        Metadata = metadata;
    }

    internal string Name { get; }
    internal Type ClrType { get; }
    internal PowerShellCompilationParameter Metadata { get; }
    internal bool IsSwitch => Metadata.IsSwitch;

    internal static PowerShellBoundParameterContract[] Bind(
        ScriptBlockAst body,
        IEnumerable<PowerShellCompilationParameter>? metadata)
    {
        var byName = (metadata ?? Array.Empty<PowerShellCompilationParameter>())
            .ToDictionary(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        return (body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>())
            .Select(parameter =>
            {
                var name = parameter.Name.VariablePath.UserPath;
                var clrType = parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                    ? typeof(bool)
                    : parameter.StaticType;
                if (!byName.TryGetValue(name, out var contract))
                {
                    contract = new PowerShellCompilationParameter(
                        name,
                        clrType.FullName ?? clrType.Name,
                        parameter.DefaultValue is not null,
                        isMandatory: false,
                        isSwitch: parameter.StaticType == typeof(System.Management.Automation.SwitchParameter),
                        aliases: null,
                        allowNull: false,
                        validations: null);
                }
                return new PowerShellBoundParameterContract(name, clrType, contract);
            })
            .ToArray();
    }
}
