using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>Records only the existing retained-entry edits for runtime authored-position projection.</summary>
internal sealed class PowerShellPackagedSourceMapping
{
    private readonly string _path;
    private readonly string _document;
    private readonly List<int> _edits = new();

    internal PowerShellPackagedSourceMapping(string path)
    {
        _path = Path.GetFullPath(path);
        _document = Parser.ParseFile(_path, out _, out _).Extent.Text;
    }

    internal void Record(int start, int length, string replacement)
    {
        _edits.Add(start);
        _edits.Add(length);
        _edits.Add(replacement.Length);
    }

    internal void Write(string workspace)
    {
        var source = "#nullable enable\nnamespace PowerForge.Compiled;\n" +
            "internal static class PowerForgePackagedSource\n{\n" +
            "    internal static global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.NativeScriptEntryLookup AddCommand(" +
            "global::System.Management.Automation.PowerShell shell, string path, string source)\n" +
            "        => global::PowerForge.Generated.Runtime.PowerShellPackagedScriptEntry.AddCommand(shell, path, source, " +
            PowerShellCSharpLiteral.QuoteString(_path) + ", " + PowerShellCSharpLiteral.QuoteString(_document) +
            ", new int[] { " + string.Join(", ", _edits) + " });\n}\n";
        File.WriteAllText(Path.Combine(workspace, "PackagedSource.g.cs"), source, new UTF8Encoding(false));
        foreach (var name in new[] { "PowerShellPackagedScriptEntry", "PowerShellNativeFunctionHost.ScriptEntryArguments" })
        {
            using var stream = typeof(PowerShellPackagedSourceMapping).Assembly.GetManifestResourceStream(
                "PowerForge.PowerShell.Compilation." + name + ".cs")
                ?? throw new InvalidOperationException("Missing retained entry runtime source: " + name);
            using var reader = new StreamReader(stream);
            File.WriteAllText(Path.Combine(workspace, name + ".g.cs"), "#nullable enable\n" + reader.ReadToEnd(), new UTF8Encoding(false));
        }
    }
}
