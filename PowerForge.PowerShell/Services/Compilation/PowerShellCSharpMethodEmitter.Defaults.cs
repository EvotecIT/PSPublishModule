using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private void EmitParameterDefaults(IReadOnlyList<ParameterAst> parameters)
    {
        foreach (var parameter in parameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            if (!_parameterMetadata.TryGetValue(name, out var metadata) || metadata.DefaultValue is null)
                continue;

            var type = GetCompiledParameterType(parameter);
            var identifier = GetVariableIdentifier(name);
            AppendLine($"if (!__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(metadata.Name)}))");
            AppendLine($"    {identifier} = {PowerShellCSharpLiteral.Emit(metadata.DefaultValue, type, GetTypeName)};");
        }
    }
}
