using AngleSharp.Html.Parser;
using PowerForge.Web;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class WebApiDocsGenericLinkNamesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
    private const string Namespace = "PowerForge.Tests.GenericLinks";

    [Theory]
    [InlineData(false, "short")]
    [InlineData(true, "short")]
    [InlineData(false, "namespace-suffix")]
    [InlineData(true, "full")]
    public void GeneratedPagesDistinguishGenericDefinitionsAndNamespaceCollisions(bool reflection, string displayMode)
    {
        Directory.CreateDirectory(root);
        XElement Type(string name, params string[] parameters) => new("member", new XAttribute("name", "T:" + name),
            new XElement("summary", "Prompt reference ", new XElement("see", new XAttribute("cref", "T:" + Namespace + ".Prompt")),
                " and ", new XElement("see", new XAttribute("cref", "T:" + Namespace + ".Prompt`1")), ". ",
                new XElement("see", new XAttribute("cref", "T:" + Namespace + ".Prompt{System.Int32}")), " and ",
                new XElement("see", new XAttribute("cref", "T:PowerForge.Tests.OtherGenericLinks.Prompt{System.Int32}")), "."),
            parameters.Select(parameter => new XElement("typeparam", new XAttribute("name", parameter), "Value.")));
        var xml = Path.Combine(root, "docs.xml");
        new XDocument(new XElement("doc", new XElement("members",
            Type(Namespace + ".Prompt"), Type(Namespace + ".Prompt`1", "T"),
            Type(Namespace + ".Dictionary`2", "TKey", "TValue"), Type(Namespace + ".ConfirmPrompt"),
            Type("PowerForge.Tests.OtherGenericLinks.Prompt"), Type("PowerForge.Tests.OtherGenericLinks.Prompt`1", "TValue")))).Save(xml);
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions
        {
            XmlPath = xml, AssemblyPath = reflection ? typeof(GenericLinks.Prompt<>).Assembly.Location : null,
            OutputPath = output, Format = "both", Template = "docs", BaseUrl = "/api",
            IncludeUndocumentedTypes = false, DisplayNameMode = displayMode
        });

        var parser = new HtmlParser();
        var pages = Directory.GetFiles(output, "*.html", SearchOption.AllDirectories);
        Assert.NotEmpty(pages);
        foreach (var path in pages)
        {
            using var doc = parser.ParseDocument(File.ReadAllText(path));
            var links = doc.QuerySelectorAll("a[href]").Where(anchor => anchor.GetAttribute("href")!.Contains("/api/"));
            var ambiguous = links.GroupBy(anchor => anchor.GetAttribute("aria-label") ?? anchor.TextContent.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(anchor => anchor.GetAttribute("href")).Distinct().Count() > 1);
            Assert.True(!ambiguous.Any(), path + ": " + string.Join(", ", ambiguous.Select(group => group.Key)));
        }
        using var generic = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "powerforge-tests-genericlinks-prompt-1", "index.html")));
        Assert.Contains("Prompt<T>", generic.QuerySelector("h1")!.TextContent);
        Assert.Contains("Prompt<T>", generic.Title);
        using var dictionary = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "powerforge-tests-genericlinks-dictionary", "index.html")));
        Assert.Contains("Dictionary<TKey, TValue>", dictionary.Title);
        Assert.Contains(dictionary.QuerySelectorAll("a"), anchor => anchor.TextContent.Trim().Contains("Dictionary<TKey, TValue>"));
        using var consumer = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "powerforge-tests-genericlinks-confirmprompt", "index.html")));
        Assert.Contains(consumer.QuerySelectorAll("a"), anchor => anchor.TextContent == "Prompt<T>" && anchor.GetAttribute("title") == Namespace + ".Prompt<T>");
        if (reflection)
            Assert.Contains(consumer.QuerySelectorAll("a"), anchor => anchor.TextContent == "Prompt<int>" &&
                anchor.GetAttribute("href") == "/api/powerforge-tests-genericlinks-prompt-1/");
        var audit = WebSiteAuditor.Audit(new WebAuditOptions { SiteRoot = root, CheckAssets = false, NavRequired = false });
        Assert.DoesNotContain(audit.Issues, issue => issue.Category == "link-purpose");
    }

    [Fact]
    public void ReflectionFallbackKeepsShortConstructedTypeLinks()
    {
        Directory.CreateDirectory(root);
        var xml = Path.Combine(root, "docs.xml");
        File.WriteAllText(xml, "<doc><members /></doc>");
        var output = Path.Combine(root, "api");
        var options = new WebApiDocsOptions { XmlPath = xml, AssemblyPath = typeof(GenericLinks.Factory).Assembly.Location,
            OutputPath = output, Format = "html", Template = "docs", BaseUrl = "/api" };
        options.IncludeNamespacePrefixes.Add(Namespace);
        WebApiDocsGenerator.Generate(options);
        using var page = new HtmlParser().ParseDocument(File.ReadAllText(Path.Combine(output, "powerforge-tests-genericlinks-factory", "index.html")));
        Assert.Contains(page.QuerySelectorAll("a"), anchor => anchor.TextContent == "Fallback<int>" &&
            anchor.GetAttribute("href") == "/api/powerforge-tests-genericlinks-fallback/");
    }

    [Theory]
    [InlineData(false, "simple")]
    [InlineData(true, "simple")]
    [InlineData(false, "docs")]
    [InlineData(true, "docs")]
    public void NestedDefinitionsPreserveDeclaringTypeParametersAndTitles(bool reflection, string template)
    {
        Directory.CreateDirectory(root);
        var xml = Path.Combine(root, "docs.xml");
        File.WriteAllText(xml, $$"""
            <doc><members>
              <member name="T:{{Namespace}}.Outer`1"><typeparam name="TOuter">Outer value.</typeparam></member>
              <member name="T:{{Namespace}}.Outer`1.Inner`1"><typeparam name="TInner">Inner value.</typeparam></member>
              <member name="T:{{Namespace}}.Factory"><summary><see cref="T:{{Namespace}}.Outer`1.Inner`1" /></summary></member>
            </members></doc>
            """);
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions { XmlPath = xml, AssemblyPath = reflection ? typeof(GenericLinks.Factory).Assembly.Location : null,
            OutputPath = output, Format = "html", Template = template, BaseUrl = "/api", IncludeUndocumentedTypes = false });
        var parser = new HtmlParser();
        var path = template == "docs" ? "powerforge-tests-genericlinks-factory/index.html" : "types/powerforge-tests-genericlinks-outer-inner.html";
        using var page = parser.ParseDocument(File.ReadAllText(Path.Combine(output, path)));
        if (template == "docs")
        {
            Assert.Contains(page.QuerySelectorAll("a"), anchor => anchor.TextContent.Contains("Inner<TInner>") && anchor.GetAttribute("title") == Namespace +
                (reflection ? ".Outer<TOuter>.Inner<TInner>" : ".Outer<T>.Inner<TInner>"));
            using var nested = parser.ParseDocument(File.ReadAllText(Path.Combine(output, "powerforge-tests-genericlinks-outer-inner", "index.html")));
            Assert.Contains("Inner<TInner>", nested.QuerySelector("h1")!.TextContent);
        }
        else
            Assert.Contains(Namespace + (reflection ? ".Outer<TOuter>.Inner<TInner>" : ".Outer<T>.Inner<TInner>"), page.Title);
    }

    [Fact]
    public void PowerShellPagesPreserveGenericTypeNamesInHelpReferences()
    {
        Directory.CreateDirectory(root);
        var xml = Path.Combine(root, "help.xml");
        File.WriteAllText(xml, """
            <helpItems xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details><command:name>Get-Prompt</command:name><maml:description><maml:para>Gets prompts.</maml:para></maml:description></command:details>
                <command:returnValues><command:returnValue><dev:type><maml:name>System.Collections.Generic.Dictionary{System.String,System.Int32}</maml:name></dev:type></command:returnValue></command:returnValues>
              </command:command>
            </helpItems>
            """);
        var output = Path.Combine(root, "api");
        WebApiDocsGenerator.Generate(new WebApiDocsOptions { Type = ApiDocsType.PowerShell, HelpPath = xml, OutputPath = output, Format = "html", Template = "docs", BaseUrl = "/api" });
        var html = File.ReadAllText(Path.Combine(output, "get-prompt", "index.html"));
        Assert.Contains("Dictionary&lt;string, int&gt;", html);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
