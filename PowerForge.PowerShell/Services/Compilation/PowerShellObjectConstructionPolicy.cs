using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Recognizes literal PowerShell object construction that has deterministic note-property semantics.</summary>
internal static class PowerShellObjectConstructionPolicy
{
    internal static bool IsLiteral(ConvertExpressionAst conversion)
        => conversion.StaticType == typeof(PSObject) &&
           conversion.Child is HashtableAst;

    internal static bool HasTypeNameMetadata(ConvertExpressionAst conversion)
        => IsLiteral(conversion) &&
           conversion.Type.TypeName.FullName.Equals("PSCustomObject", StringComparison.OrdinalIgnoreCase) &&
           ((HashtableAst)conversion.Child).KeyValuePairs.Any(pair =>
            pair.Item1 is StringConstantExpressionAst key &&
            key.Value.Equals("PSTypeName", StringComparison.OrdinalIgnoreCase));
}
