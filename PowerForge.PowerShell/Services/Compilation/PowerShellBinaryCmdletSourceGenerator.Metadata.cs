using System.Text;
using PowerForge.Compilation.Build;

namespace PowerForge;

internal static partial class PowerShellBinaryCmdletSourceGenerator
{
    internal static PowerShellCommandMetadataNames.Identity[] GetCommandIdentities(
        PowerShellTypedCompilationResult typed, IEnumerable<string>? exportedFunctions)
    {
        var selected = exportedFunctions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return typed.Methods.Where(method => selected is null || selected.Contains(method.SourceName))
            .Select(CreateDescriptor)
            .Select(cmdlet => new PowerShellCommandMetadataNames.Identity(
                typed.NamespaceName + "." + cmdlet.ClassName.TrimStart('@'),
                cmdlet.Method.SourceName,
                GetCommandIdentityStorage(cmdlet.Method)))
            .ToArray();
    }

    private static string GetCommandIdentityStorage(PowerShellCompiledMethod method)
    {
        var bytes = Encoding.UTF8.GetBytes(method.SourceName);
        var name = "__PowerForgeCommandIdentity_" + string.Concat(bytes.Select(static value => value.ToString("x2"))) + "_Storage";
        var parameters = method.Parameters.Select(static parameter => PowerShellCSharpSymbolRenderer.Identifier(parameter.Name).TrimStart('@'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (parameters.Contains(name)) name += "_";
        return name;
    }
}
