using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorDependencyIsolationTests
{
    [Theory]
    [InlineData("Scriban", false)]
    [InlineData("Scriban", true)]
    [InlineData("System.Text.Json", false)]
    [InlineData("System.Text.Json", true)]
    public void Generate_UsesNewerDependencyFromAdditionalDocumentedAssembly(string dependencyName, bool useDependencyManifest)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var hostDependency = dependencyName == "Scriban" ? typeof(Scriban.Template).Assembly : typeof(JsonDocument).Assembly;
            var hostName = hostDependency.GetName();
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(hostDependency));
            var version = new Version(hostName.Version!.Major + 1, 0, 0, 0);
            var primaryDirectory = Directory.CreateDirectory(Path.Combine(root, "primary")).FullName;
            var additionalDirectory = Directory.CreateDirectory(Path.Combine(root, "additional")).FullName;
            var olderDependency = Compile(primaryDirectory, dependencyName, $$"""
                [assembly: System.Reflection.AssemblyVersion("{{hostName.Version}}")]
                namespace DependencyFixture { public class BasePayload { } }
                """);
            var packageName = dependencyName.ToLowerInvariant();
            var dependencyDirectory = useDependencyManifest
                ? Directory.CreateDirectory(Path.Combine(root, ".nuget", "packages", packageName, version.ToString(), "lib", "net8.0")).FullName
                : additionalDirectory;
            var dependency = Compile(dependencyDirectory, hostName.Name!, $$"""
                [assembly: System.Reflection.AssemblyVersion("{{version}}")]
                namespace DependencyFixture { public class BasePayload { } public class NewerPayload : BasePayload { } }
                """);
            var primary = Compile(primaryDirectory, "PrimaryApi", "namespace Fixture { public class Primary : DependencyFixture.BasePayload { } }", olderDependency);
            var additional = Compile(additionalDirectory, "AdditionalApi", """
                namespace Fixture
                {
                    public class Adapter : DependencyFixture.NewerPayload
                    {
                        public DependencyFixture.NewerPayload Read(DependencyFixture.NewerPayload payload) => payload;
                    }
                }
                """, dependency);
            if (useDependencyManifest)
            {
                File.WriteAllText(Path.ChangeExtension(additional, ".deps.json"), $$"""
                    {
                      "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0", "signature": "" },
                      "compilationOptions": {},
                      "targets": { ".NETCoreApp,Version=v8.0": {
                        "AdditionalApi/1.0.0": {
                          "dependencies": { "{{dependencyName}}": "{{version}}" },
                          "runtime": { "AdditionalApi.dll": {} }
                        },
                        "{{dependencyName}}/{{version}}": { "runtime": { "lib/net8.0/{{dependencyName}}.dll": {} } }
                      } },
                      "libraries": {
                        "AdditionalApi/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "{{dependencyName}}/{{version}}": {
                        "type": "package", "serviceable": true, "sha512": "", "path": "{{packageName}}/{{version}}", "hashPath": "{{packageName}}.{{version}}.nupkg.sha512"
                      } }
                    }
                    """);
            }
            var xml = Path.Combine(root, "api.xml");
            File.WriteAllText(xml, """
                <doc><assembly><name>AdditionalApi</name></assembly><members>
                  <member name="T:Fixture.Adapter"><summary>Reads newer payloads.</summary></member>
                  <member name="M:Fixture.Adapter.Read(DependencyFixture.NewerPayload)"><summary>Reads a payload.</summary></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPaths = new[] { primary, additional },
                XmlPath = xml,
                OutputPath = output,
                Format = "json",
                IncludeUndocumentedTypes = false
            });

            Assert.Equal(1, result.TypeCount);
            Assert.Empty(result.Warnings);
            using var document = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(output, "types"), "*.json"))));
            var type = document.RootElement;
            Assert.Equal("Fixture.Adapter", type.GetProperty("fullName").GetString());
            var method = Assert.Single(type.GetProperty("methods").EnumerateArray(), value => value.GetProperty("name").GetString() == "Read");
            Assert.Equal("payload", method.GetProperty("parameters")[0].GetProperty("name").GetString());
            Assert.Contains("NewerPayload", method.GetProperty("signature").GetString());
            Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == hostName.Name && assembly.GetName().Version == version);
            Assert.Same(hostDependency, AssemblyLoadContext.Default.LoadFromAssemblyName(hostName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_ReportsMalformedManifestForAdditionalAssembly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-apidocs-manifest-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var primary = Compile(root, "PrimaryApi", "public class Primary { }");
            var additional = Compile(root, "AdditionalApi", "public class Additional { }");
            var manifest = Path.ChangeExtension(additional, ".deps.json");
            File.WriteAllText(manifest, "{ malformed");
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><members/></doc>");

            var error = Assert.Throws<InvalidOperationException>(() => WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPaths = new[] { primary, additional }, XmlPath = xml,
                OutputPath = Path.Combine(root, "api"), Format = "json"
            }));

            Assert.Contains(manifest, error.Message, StringComparison.Ordinal);
            Assert.Contains("could not be parsed", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Compile(string directory, string name, string source, string? dependency = null)
    {
        var references = new[] { typeof(object).Assembly.Location, dependency }
            .Where(path => path is not null).Select(path => MetadataReference.CreateFromFile(path!));
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, name + ".dll");
        var emitted = compilation.Emit(path);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return path;
    }
}
