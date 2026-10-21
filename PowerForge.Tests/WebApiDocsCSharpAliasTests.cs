using System.Text.Json;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsCSharpAliasTests : IDisposable
{
    private const string Owner = "PowerForge.Tests.CSharpAliases.Fixture";
    private readonly string root = Path.Combine(Path.GetTempPath(), "pf-api-csharp-aliases-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] PrimitiveTypes =
    {
        "System.Boolean", "System.Byte", "System.SByte", "System.Char", "System.Decimal", "System.Double",
        "System.Single", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Int16",
        "System.UInt16", "System.IntPtr", "System.UIntPtr", "System.String", "System.Object"
    };
    private static readonly string[] Keywords =
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint", "long", "ulong",
        "short", "ushort", "nint", "nuint", "string", "object"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_UsesCSharpKeywordsWithoutChangingPrimitiveUidsOrAnchors(bool useReflection)
    {
        var uid = $"M:{Owner}.Primitives({string.Join(",", PrimitiveTypes)})";
        var output = Generate(useReflection, new XElement("member", new XAttribute("name", uid),
            new XElement("summary", "String and Int32 are literal documentation text.")));
        using var json = ReadType(output, "fixture");
        var method = Method(json, "Primitives");
        Assert.Equal(Keywords, method.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("type").GetString()));
        if (useReflection)
        {
            Assert.Equal("void", method.GetProperty("returnType").GetString());
            Assert.StartsWith("public void Primitives(bool boolean, byte octet, sbyte signedOctet, char character, decimal money, double precise, float single, int integer, uint unsignedInteger, long large, ulong unsignedLarge, short small, ushort unsignedSmall, nint native, nuint unsignedNative, string text, object value)",
                method.GetProperty("signature").GetString());
        }
        var html = ReadHtml(output, "fixture");
        Assert.Contains("String and Int32 are literal documentation text.", html);
        Assert.Contains(useReflection ? "bool boolean" : "bool arg1", html);
        Assert.Contains("<li>object</li>", ReadHtml(output, "string"));
        AssertUidAndAnchor(output, html, uid,
            "method-primitives-boolean-byte-sbyte-char-decimal-double-single-int32-uint32-int64-uint64-int16-uint16-intptr-uintptr-string-object");
    }

    [Fact]
    public void Generate_ReflectsNullableArraysGenericsReturnsAndInheritanceWithKeywords()
    {
        var output = Generate(true,
            Member($"M:{Owner}.Transform(System.String[],System.Nullable{{System.Int32}},System.String[0:,0:])"),
            Member($"M:{Owner}.Object(System.Object)"),
            Member($"M:{Owner}.#ctor(System.Int32,System.String)"),
            Member($"P:{Owner}.Name"), Member($"F:{Owner}.Count"));
        using var json = ReadType(output, "fixture");
        var transform = Method(json, "Transform");
        Assert.Equal("Dictionary<string, List<int?[]?>?>?", transform.GetProperty("returnType").GetString());
        Assert.Equal("public Dictionary<string, List<int?[]?>?>? Transform(string?[]? text, int? count, string?[,]? matrix)",
            transform.GetProperty("signature").GetString());
        Assert.Equal(new[] { "string?[]?", "int?", "string?[,]?" },
            transform.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("type").GetString()));
        Assert.Equal("object?", Method(json, "Object").GetProperty("returnType").GetString());
        Assert.Equal("public Fixture(int count, string text)", json.RootElement.GetProperty("constructors")[0].GetProperty("signature").GetString());
        Assert.Equal("public string? Name { get; set; }", json.RootElement.GetProperty("properties")[0].GetProperty("signature").GetString());
        Assert.Equal("public int? Count", json.RootElement.GetProperty("fields")[0].GetProperty("signature").GetString());
        var html = ReadHtml(output, "fixture");
        Assert.Contains("Dictionary&lt;string, List&lt;int?[]?&gt;?&gt;?", html);
        Assert.Contains("AliasBase&lt;Dictionary&lt;string, List&lt;int?[]&gt;&gt;&gt;", html);
        Assert.Contains("<code>object?</code>", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_PreservesNamedTypesMemberNamesAndLinksInsideNullableExpressions(bool useReflection)
    {
        var uid = $"M:{Owner}.String(System.Nullable{{PowerForge.Tests.CSharpAliases.Int32}},System.String)";
        var output = Generate(useReflection, Member(uid),
            Member($"M:{Owner}.Nested(System.Collections.Generic.Dictionary{{System.String,System.Nullable{{PowerForge.Tests.CSharpAliases.Int32}}[]}})"));
        using var json = ReadType(output, "fixture");
        var method = Method(json, "String");
        Assert.Equal("String", method.GetProperty("name").GetString());
        Assert.Equal("PowerForge.Tests.CSharpAliases.Int32?", method.GetProperty("parameters")[0].GetProperty("type").GetString());
        var html = ReadHtml(output, "fixture");
        if (useReflection)
        {
            Assert.Equal("String?", method.GetProperty("returnType").GetString());
            Assert.Equal("public String? String(Int32? value, string text = \"String System.Int32\")", method.GetProperty("signature").GetString());
            Assert.Contains("href=\"/api/powerforge-tests-csharpaliases-string/\">String</a>?", html);
            Assert.Contains("href=\"/api/powerforge-tests-csharpaliases-string/\">String</a>", ReadHtml(output, "customderived"));
        }
        else
        {
            var nested = Method(json, "Nested").GetProperty("parameters")[0].GetProperty("type").GetString();
            Assert.Equal("System.Collections.Generic.Dictionary<string, PowerForge.Tests.CSharpAliases.Int32?[]>", nested);
            Assert.Contains("Dictionary&lt;string, PowerForge.Tests.CSharpAliases.Int32?[]&gt;", html);
        }
        AssertUidAndAnchor(output, html, uid, "method-string-nullable-powerforge-tests-csharpaliases-int32-string");
        using var xref = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "xrefmap.json")));
        Assert.Contains(xref.RootElement.GetProperty("references").EnumerateArray(),
            item => item.GetProperty("uid").GetString() == "PowerForge.Tests.CSharpAliases.String" &&
                item.GetProperty("aliases").EnumerateArray().Any(alias => alias.GetString() == "T:PowerForge.Tests.CSharpAliases.String"));
    }

    [Fact]
    public void Generate_XmlOnlyPreservesTypeAndMethodGenericParameterReferences()
    {
        const string genericOwner = "PowerForge.Tests.CSharpAliases.Generic`1";
        const string uid = "M:" + genericOwner + ".Echo``1(`0,``0,System.Collections.Generic.Dictionary{System.String,System.Collections.Generic.List{``0}})";
        var output = Generate(false, Member("T:" + genericOwner), Member(uid));
        var path = Directory.GetFiles(Path.Combine(output, "types"), "*.json")
            .Single(file => Path.GetFileName(file).StartsWith("powerforge-tests-csharpaliases-generic", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var method = Method(json, "Echo``1");
        Assert.Equal(new[] { "`0", "``0", "System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<``0>>" },
            method.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("type").GetString()));
        // XML-only JSON has parameter types; its display signature is composed by the HTML renderer.
        var html = File.ReadAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path), "index.html"));
        Assert.Contains("`0 arg1", html);
        Assert.Contains("``0 arg2", html);
        Assert.Contains("List&lt;``0&gt;", html);
        using var xref = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "xrefmap.json")));
        Assert.Contains(xref.RootElement.GetProperty("references").EnumerateArray(),
            item => item.GetProperty("uid").GetString() == uid);
    }

    private string Generate(bool useReflection, params XElement[] members)
    {
        Directory.CreateDirectory(root);
        var xml = Path.Combine(root, "docs.xml");
        new XDocument(new XElement("doc", new XElement("members",
            Member("T:" + Owner), Member("T:PowerForge.Tests.CSharpAliases.String"),
            Member("T:PowerForge.Tests.CSharpAliases.Int32"), Member("T:PowerForge.Tests.CSharpAliases.CustomDerived"), members)))
            .Save(xml);
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions
        {
            XmlPath = xml, AssemblyPath = useReflection ? typeof(CSharpAliases.Fixture).Assembly.Location : null,
            OutputPath = output, Format = "both", Template = "docs", BaseUrl = "/api", IncludeUndocumentedTypes = false
        });
        return output;
    }

    private static XElement Member(string uid) => new("member", new XAttribute("name", uid), new XElement("summary", "Alias fixture."));
    private static JsonDocument ReadType(string output, string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "powerforge-tests-csharpaliases-" + name + ".json")));
    private static string ReadHtml(string output, string name) => File.ReadAllText(Path.Combine(output, "powerforge-tests-csharpaliases-" + name, "index.html"));
    private static JsonElement Method(JsonDocument json, string name) => json.RootElement.GetProperty("methods").EnumerateArray().Single(method => method.GetProperty("name").GetString() == name);

    private static void AssertUidAndAnchor(string output, string html, string uid, string expectedAnchor)
    {
        using var xref = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "xrefmap.json")));
        var reference = Assert.Single(xref.RootElement.GetProperty("references").EnumerateArray(), item => item.GetProperty("uid").GetString() == uid);
        var href = reference.GetProperty("href").GetString()!;
        Assert.EndsWith("#" + expectedAnchor, href);
        Assert.Contains($"id=\"{expectedAnchor}\"", html);
        Assert.Contains(uid[2..], reference.GetProperty("aliases").EnumerateArray().Select(alias => alias.GetString()));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
