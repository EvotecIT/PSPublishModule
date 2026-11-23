using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Shares exact authored documents between generated native command and callback sites.</summary>
internal sealed class PowerShellCSharpSourceDocumentPool
{
    private readonly Dictionary<string, string> _references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string> Documents => _documents;

    internal string Reference(string source)
    {
        if (_references.TryGetValue(source, out var name)) return name;
        using var sha = SHA256.Create();
        name = "__pf_source_document_" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(source))
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        if (_documents.TryGetValue(name, out var previous) && !previous.Equals(source, StringComparison.Ordinal))
            throw new InvalidOperationException("Authored source document identities conflict.");
        _documents[name] = source;
        _references[source] = name;
        return name;
    }

    /// <summary>Emits only documents referenced by the selected complete methods and their private callbacks.</summary>
    internal static string[] Declarations(IEnumerable<PowerShellCSharpMethodEmission> emissions)
    {
        var methods = emissions.ToArray();
        return Declarations(methods.Select(static method => method.Source).ToArray(),
            methods.Select(static method => method.NativeSourceDocuments));
    }

    internal static string[] Declarations(string[] methods,
        IEnumerable<IReadOnlyDictionary<string, string>> documents)
        => documents.SelectMany(static document => document)
            .GroupBy(static item => item.Key, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Where(group => methods.Any(method => method.Contains(group.Key)))
            .Select(static group => "    private const string " + group.Key + " = " +
                PowerShellCSharpLiteral.QuoteString(group.First().Value) + ";")
            .ToArray();
}
