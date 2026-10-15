using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PowerForge.Web;

/// <summary>Locates declarations without IL sequence points in checksum-verified PDB documents.</summary>
internal sealed class CSharpSourceDeclarationIndex
{
    internal sealed record Location(string Path, int Line);
    private readonly Dictionary<string, List<Location>> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Location?> _fields = new(StringComparer.Ordinal);

    internal static CSharpSourceDeclarationIndex Create(MetadataReader reader, string? sourceRoot)
    {
        var index = new CSharpSourceDeclarationIndex();
        var options = ReadParseOptions(reader);
        foreach (var handle in reader.Documents)
        {
            var document = reader.GetDocument(handle);
            var pdbPath = reader.GetString(document.Name);
            if (!pdbPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            var path = ResolveDocumentPath(pdbPath, sourceRoot);
            if (path is null || !File.Exists(path) || document.Hash.IsNil || document.HashAlgorithm.IsNil) continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var algorithm = reader.GetGuid(document.HashAlgorithm);
                var checksum = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(bytes)
                    : algorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460") ? SHA1.HashData(bytes) : null;
                if (checksum is null || !checksum.AsSpan().SequenceEqual(reader.GetBlobBytes(document.Hash))) continue;
                using var stream = new MemoryStream(bytes);
                using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var tree = CSharpSyntaxTree.ParseText(textReader.ReadToEnd(), options, path);
                index.AddDeclarations(tree);
            }
            catch (IOException) { /* Unavailable source must not invent a link. */ }
            catch (UnauthorizedAccessException) { }
        }
        return index;
    }

    internal Location? FindType(Type type)
    {
        if (!_types.TryGetValue(TypeKey(type), out var locations)) return null;
        var stem = type.Name.Split('`')[0];
        return locations.OrderByDescending(location => FileRank(location.Path, stem))
            .ThenBy(location => location.Path, StringComparer.Ordinal).ThenBy(location => location.Line).FirstOrDefault();
    }

    internal Location? FindField(FieldInfo field) => field.DeclaringType is not null &&
        _fields.TryGetValue(TypeKey(field.DeclaringType) + "." + field.Name, out var location) ? location : null;

    private static string TypeKey(Type type) => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? type.Name;

    private void AddDeclarations(SyntaxTree tree)
    {
        foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var key = DeclarationKey(declaration);
            var location = Locate(tree, declaration.Identifier.SpanStart);
            if (!_types.TryGetValue(key, out var locations)) _types[key] = locations = new List<Location>();
            locations.Add(location);
            if (declaration is EnumDeclarationSyntax enumeration)
                foreach (var member in enumeration.Members) AddField(key + "." + member.Identifier.ValueText, Locate(tree, member.Identifier.SpanStart));
            if (declaration is TypeDeclarationSyntax type)
                foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
                    foreach (var variable in field.Declaration.Variables)
                        AddField(key + "." + variable.Identifier.ValueText, Locate(tree, variable.Identifier.SpanStart));
        }
    }

    private void AddField(string key, Location location)
    {
        if (!_fields.TryAdd(key, location) && _fields[key] != location) _fields[key] = null;
    }

    private static Location Locate(SyntaxTree tree, int position) => new(tree.FilePath, tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0)).StartLinePosition.Line + 1);

    private static string DeclarationKey(BaseTypeDeclarationSyntax declaration)
    {
        var types = declaration.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse()
            .Select(type => type.Identifier.ValueText + (type is TypeDeclarationSyntax { TypeParameterList: { Parameters.Count: > 0 } parameters }
                ? "`" + parameters.Parameters.Count : string.Empty));
        var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(ns => string.Concat(ns.Name.DescendantTokens().Select(token => token.ValueText)));
        var prefix = string.Join(".", namespaces);
        return (prefix.Length == 0 ? string.Empty : prefix + ".") + string.Join("+", types);
    }

    private static int FileRank(string path, string stem)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Equals(stem, StringComparison.OrdinalIgnoreCase) ? 2
            : name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static string? ResolveDocumentPath(string path, string? root)
    {
        var portable = path.Replace('\\', '/');
        var prefix = portable.StartsWith("/_/", StringComparison.Ordinal) ? 3
            : portable.Length > 5 && char.IsAsciiLetter(portable[0]) && portable.AsSpan(1, 4).SequenceEqual(":/_/") ? 5 : 0;
        if (prefix == 0) return Path.IsPathRooted(path) ? path : root is null ? null : Path.Combine(root, path);
        if (root is null) return null;
        var candidate = Path.GetFullPath(Path.Combine(root, portable[prefix..]));
        var relative = Path.GetRelativePath(root, candidate);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : candidate;
    }

    // Roslyn stores the original conditional symbols and language version as UTF-8 key/value pairs.
    private static CSharpParseOptions ReadParseOptions(MetadataReader reader)
    {
        var options = CSharpParseOptions.Default;
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(info.Kind) != new Guid("b5feec05-8cd0-4a83-96da-466284bb4bd8")) continue;
            var values = Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value)).Split('\0');
            for (var i = 0; i + 1 < values.Length; i += 2)
            {
                if (values[i] == "define") options = options.WithPreprocessorSymbols(values[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries));
                if (values[i] == "language-version" && LanguageVersionFacts.TryParse(values[i + 1], out var version)) options = options.WithLanguageVersion(version);
            }
        }
        return options;
    }
}
