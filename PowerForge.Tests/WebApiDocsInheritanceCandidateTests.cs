using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsInheritanceCandidateTests
{
    [Fact]
    public void Generate_SkipsUndocumentedInterfaceForDocumentedMethodPropertyAndEvent()
    {
        var json = Generate("""
            <member name="T:PowerForge.Tests.WebCandidateMultiContract"><summary>Implementation.</summary></member>
            <member name="M:PowerForge.Tests.WebCandidateMultiContract.Run"><inheritdoc/></member>
            <member name="P:PowerForge.Tests.WebCandidateMultiContract.Name"><inheritdoc/></member>
            <member name="E:PowerForge.Tests.WebCandidateMultiContract.Changed"><inheritdoc/></member>
            <member name="M:PowerForge.Tests.IWebCandidateB.Run"><summary>Documented operation.</summary></member>
            <member name="P:PowerForge.Tests.IWebCandidateB.Name"><summary>Documented name.</summary></member>
            <member name="E:PowerForge.Tests.IWebCandidateB.Changed"><summary>Documented change.</summary></member>
            """, "webcandidatemulticontract");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Documented operation.", document.RootElement.GetProperty("methods").EnumerateArray().Single(method => !method.GetProperty("isInherited").GetBoolean()).GetProperty("summary").GetString());
        Assert.Equal("Documented name.", Assert.Single(document.RootElement.GetProperty("properties").EnumerateArray()).GetProperty("summary").GetString());
        Assert.Equal("Documented change.", Assert.Single(document.RootElement.GetProperty("events").EnumerateArray()).GetProperty("summary").GetString());
    }

    [Fact]
    public void Generate_SkipsUndocumentedIntermediateBaseForDocumentedAncestor()
    {
        var json = Generate("""
            <member name="T:PowerForge.Tests.WebCandidateDerived"><inheritdoc/></member>
            <member name="M:PowerForge.Tests.WebCandidateDerived.ToString"><inheritdoc/></member>
            <member name="T:PowerForge.Tests.WebCandidateRoot"><summary>Documented ancestor.</summary></member>
            <member name="M:PowerForge.Tests.WebCandidateRoot.ToString"><summary>Documented representation.</summary></member>
            """, "webcandidatederived");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Documented ancestor.", document.RootElement.GetProperty("summary").GetString());
        Assert.Equal("Documented representation.", document.RootElement.GetProperty("methods").EnumerateArray().Single(method => !method.GetProperty("isInherited").GetBoolean()).GetProperty("summary").GetString());
    }

    [Fact]
    public void Generate_SkipsDistantFrameworkCandidateButContinuesToDocumentedLibraryInterface()
    {
        var json = Generate("""
            <member name="T:PowerForge.Tests.WebCandidateInterfaceString"><summary>Implementation.</summary></member>
            <member name="M:PowerForge.Tests.WebCandidateInterfaceString.ToString"><inheritdoc/></member>
            <member name="M:PowerForge.Tests.IWebCandidateString.ToString"><summary>Documented interface representation.</summary></member>
            """, "webcandidateinterfacestring");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Documented interface representation.", document.RootElement.GetProperty("methods").EnumerateArray().Single(method => !method.GetProperty("isInherited").GetBoolean()).GetProperty("summary").GetString());
    }

    private static string Generate(string members, string slug)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-candidates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><members>" + members + "</members></doc>");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = typeof(WebCandidateMultiContract).Assembly.Location,
                OutputPath = Path.Combine(root, "api"), Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Implicit inheritdoc", StringComparison.Ordinal));
            return File.ReadAllText(Path.Combine(root, "api", "types", "powerforge-tests-" + slug + ".json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public interface IWebCandidateA
{
    void Run();
    string Name { get; }
    event EventHandler Changed;
}

public interface IWebCandidateB
{
    void Run();
    string Name { get; }
    event EventHandler Changed;
}

public sealed class WebCandidateMultiContract : IWebCandidateA, IWebCandidateB
{
    public void Run() { }
    public string Name => "Candidate";
    public event EventHandler Changed { add { } remove { } }
}

public class WebCandidateRoot
{
    public override string ToString() => "Root";
}

public class WebCandidateMiddle : WebCandidateRoot
{
    public override string ToString() => "Middle";
}

public sealed class WebCandidateDerived : WebCandidateMiddle
{
    public override string ToString() => "Derived";
}

public interface IWebCandidateString
{
    string ToString();
}

public sealed class WebCandidateInterfaceString : WebMissingLibraryDocsBase, IWebCandidateString
{
    public override string ToString() => "Implementation";
}
