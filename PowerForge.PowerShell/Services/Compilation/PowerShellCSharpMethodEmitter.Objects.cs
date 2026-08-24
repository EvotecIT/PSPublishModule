using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private string EmitPowerShellObject(ConvertExpressionAst conversion)
    {
        if (!_capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) ||
            conversion.Child is not HashtableAst hashtable)
            throw Error(conversion, "[pscustomobject] literals require a generated binary-module host.");

        var identifier = "__powerForgeObject" + _objectIndex++;
        var statements = new List<string>
        {
            $"var {identifier} = new global::System.Management.Automation.PSObject();"
        };
        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (pair.Item1 is not StringConstantExpressionAst key || string.IsNullOrWhiteSpace(key.Value))
                throw Error(pair.Item1, "Typed [pscustomobject] literals require non-empty literal string property names.");
            var value = GetHashtableValue(pair.Item2);
            if (InferExpressionType(value) == typeof(void))
                throw Error(value, "Typed [pscustomobject] note-property values must produce a CLR value.");
            statements.Add(
                $"{identifier}.Properties.Add(new global::System.Management.Automation.PSNoteProperty(" +
                $"{PowerShellCSharpLiteral.QuoteString(key.Value)}, {EmitExpression(value)}));");
        }
        statements.Add($"return {identifier};");
        return "new global::System.Func<global::System.Management.Automation.PSObject>(() => { " +
               string.Join(" ", statements) + " })()";
    }
}
