using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitNativeDictionary(PowerShellLoweredDictionaryExpression dictionary)
    {
        var type = PowerShellCSharpSymbolRenderer.TypeName(dictionary.ClrType);
        var temporary = _getTemporaryIdentifier?.Invoke("__nativeMap")
            ?? throw new InvalidOperationException("Native dictionary construction requires a function temporary allocator.");
        var ordered = dictionary.Kind == PowerShellBoundDictionaryKind.NativeOrderedDictionary;
        var body = new StringBuilder("new global::System.Func<").Append(type).Append(">(() => { var ")
            .Append(temporary).Append(" = (").Append(type).Append(")__nativeFunction.CreateDictionary(")
            .Append(ordered ? "true" : "false").Append(", ").Append(dictionary.Entries.Length).Append("); ");
        foreach (var entry in dictionary.Entries)
            body.Append("__nativeFunction.AddDictionaryEntry(").Append(temporary).Append(", ")
                .Append(EmitExpression(entry.Key)).Append(", ").Append(EmitExpression(entry.Value)).Append(", ")
                .Append(EmitSourceExtentArguments(entry.Key.Span)).Append("); ");
        return body.Append("return ").Append(temporary).Append("; })()").ToString();
    }
}
