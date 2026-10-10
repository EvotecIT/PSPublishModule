using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public sealed class WebApiDocsGeneratorMemberCrefTests
{
    [Fact]
    public void GenerateDocsHtml_RendersMemberCrefsInFullDescriptionsAndSearch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "sample.xml");
            File.WriteAllText(xml, """
                <doc><assembly><name>Sample</name></assembly><members>
                <member name="T:Example.PdfDocument"><summary>Use <see cref="M:Example.PdfDocument.Create(Example.PdfOptions)"/> or <see cref="M:Example.PdfDocument.Open"/> and <see cref="P:Example.PdfDocument.Pages"/> with <see cref="T:Example.PdfOptions"/>.</summary></member>
                <member name="T:Example.PdfOptions"><summary>Options.</summary></member>
                <member name="M:Example.PdfDocument.Save(System.String)"><summary>See <see cref="M:Example.PdfDocument.Open"/>.</summary><param name="path">Use <see cref="P:Example.PdfDocument.Pages"/>.</param><returns>Created by <see cref="M:Example.PdfDocument.Create(Example.PdfOptions)"/>.</returns></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions { XmlPath = xml, OutputPath = output, Format = "html", Template = "docs", BaseUrl = "/api" });
            var html = File.ReadAllText(Path.Combine(output, "example-pdfdocument", "index.html"));
            Assert.Contains(">PdfDocument.Create</a>", html);
            Assert.Contains(">PdfDocument.Open</a>", html);
            Assert.Contains(">PdfDocument.Pages</a>", html);
            Assert.Contains(">PdfOptions</a>", html);
            Assert.DoesNotContain("[[cref:", html);
            Assert.DoesNotContain("PdfOptions)", html);
            Assert.Equal("PdfDocument.Open PdfDocument.Pages", WebApiDocsGenerator.StripCrefTokens("[[cref:M:Example.PdfDocument.Open]] [[cref:P:Example.PdfDocument.Pages]]"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
