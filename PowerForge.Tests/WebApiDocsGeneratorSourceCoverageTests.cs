using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorSourceCoverageTests
{
    [Fact]
    public void SourceScopeRetainsMembersFromSuppliedBaseAssemblyWhenItsTypesAreFilteredOut()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-source-multi-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string Compile(string name, string source, string? dependency = null)
            {
                var references = new[] { typeof(object).Assembly.Location, dependency }.Where(path => path is not null)
                    .Select(path => MetadataReference.CreateFromFile(path!));
                var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var dll = Path.Combine(root, name + ".dll");
                var emitted = compilation.Emit(dll);
                Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
                return dll;
            }
            var core = Compile("OwnedCore", "namespace Owned.Core { public class Base { public int Owned() => 1; } }");
            var api = Compile("OwnedApi", "namespace Owned.Api { public class Derived : Owned.Core.Base { } }", core);
            string Xml(string assembly, string type)
            {
                var path = Path.Combine(root, assembly + ".xml");
                File.WriteAllText(path, $"<doc><assembly><name>{assembly}</name></assembly><members><member name=\"T:{type}\"><summary>Owned.</summary></member></members></doc>");
                return path;
            }
            var options = new WebApiDocsOptions
            {
                AssemblyPaths = new[] { api, core },
                XmlPaths = new[] { Xml("OwnedApi", "Owned.Api.Derived"), Xml("OwnedCore", "Owned.Core.Base") },
                OutputPath = Path.Combine(root, "api"), Format = "json", IncludeUndocumentedTypes = false,
                SourceMemberCoverageScope = "assembly"
            };
            options.IncludeTypeNames.Add("Owned.Api.Derived");
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Equal(1, result.TypeCount);
            using var report = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var coverage = report.RootElement.GetProperty("source").GetProperty("members");
            Assert.Equal(2, coverage.GetProperty("count").GetInt32()); // Derived constructor and Base.Owned.
            Assert.Equal(0, coverage.GetProperty("url").GetProperty("covered").GetInt32());
            Assert.Equal(4, coverage.GetProperty("excludedExternalMembers").GetInt32()); // System.Object only.
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("all")]
    [InlineData("assembly")]
    public void SourceMemberScopeKeepsInheritedApiVisibleAndCountsMissingOwnedLinks(string scope)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-source-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "fixture.xml");
            File.WriteAllText(xml, """<doc><members><member name="T:PowerForge.Tests.ApiDocsSourceFixtures.SourceFixtureDerived"><summary>Derived.</summary></member></members></doc>""");
            var options = new WebApiDocsOptions
            {
                XmlPath = xml,
                AssemblyPath = typeof(PowerForge.Tests.ApiDocsSourceFixtures.SourceFixtureDerived).Assembly.Location,
                OutputPath = Path.Combine(root, "api"),
                Format = "json",
                IncludeUndocumentedTypes = false,
                SourceMemberCoverageScope = scope
                // No source URL: owned declarations must still count as missing.
            };
            var result = WebApiDocsGenerator.Generate(options);
            using var api = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "types", "powerforge-tests-apidocssourcefixtures-sourcefixturederived.json")));
            var methods = api.RootElement.GetProperty("methods").EnumerateArray().ToArray();
            Assert.Contains(methods, method => method.GetProperty("declaringType").GetString() == "System.Object");
            Assert.Contains(methods, method => method.GetProperty("name").GetString() == "Describe");
            using var report = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var coverage = report.RootElement.GetProperty("source").GetProperty("members");
            var total = report.RootElement.GetProperty("members").GetProperty("count").GetInt32();
            var excluded = methods.Count(method => method.GetProperty("declaringType").GetString() == "System.Object");
            Assert.Equal(scope == "all" ? 0 : excluded, coverage.GetProperty("excludedExternalMembers").GetInt32());
            Assert.Equal(scope == "all" ? total : total - excluded, coverage.GetProperty("count").GetInt32());
            Assert.Equal(0, coverage.GetProperty("url").GetProperty("covered").GetInt32());
            Assert.True(coverage.GetProperty("url").GetProperty("total").GetInt32() > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../../../../_/CodeGlyphX/Barcode.cs")]
    [InlineData("../../CodeGlyphX/CodeGlyphX/Barcode.cs")]
    public void GitHubRepoMismatchHint_IgnoresLeadingRelativePdbSegments(string sourcePath)
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(sourcePath, url, out var hint);

        Assert.False(mismatch);
        Assert.Equal(string.Empty, hint);
    }

    [Theory]
    [InlineData("../../WrongRepository/WrongRepository/Barcode.cs")]
    [InlineData("../../../../_/WrongRepository/WrongRepository/Barcode.cs")]
    public void GitHubRepoMismatchHint_StillDetectsWrongRepositoryAfterPortablePdbPrefixes(string sourcePath)
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(
            sourcePath,
            url,
            out var hint);

        Assert.True(mismatch);
        Assert.Equal("WrongRepository -> CodeGlyphX", hint);
    }
}
