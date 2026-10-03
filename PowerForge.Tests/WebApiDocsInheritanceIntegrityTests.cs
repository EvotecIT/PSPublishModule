using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsInheritanceIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssemblyResolvesImplicitInterfaceAndBaseDocumentationAndCSharpModifiers(bool separateContracts)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-inheritance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:PowerForge.Tests.IWebInheritanceContract`1"><summary>Contract type.</summary></member>
                  <member name="M:PowerForge.Tests.IWebInheritanceContract`1.Run(`0)"><summary>Run inherited summary.</summary><param name="input">Inherited input.</param><returns>Inherited return.</returns></member>
                  <member name="P:PowerForge.Tests.IWebInheritanceContract`1.Name"><summary>Inherited name.</summary></member>
                  <member name="E:PowerForge.Tests.IWebInheritanceContract`1.Changed"><summary>Inherited event.</summary></member>
                  <member name="T:PowerForge.Tests.WebInheritanceBase"><summary>Base type.</summary></member>
                  <member name="M:PowerForge.Tests.WebInheritanceBase.Execute(System.String)"><summary>Base execution.</summary><param name="input">Base input.</param></member>
                  <member name="T:PowerForge.Tests.WebInheritanceImplementation"><inheritdoc/></member>
                  <member name="M:PowerForge.Tests.WebInheritanceImplementation.Run(System.String)"><inheritdoc/></member>
                  <member name="P:PowerForge.Tests.WebInheritanceImplementation.Name"><inheritdoc/></member>
                  <member name="E:PowerForge.Tests.WebInheritanceImplementation.Changed"><inheritdoc/></member>
                  <member name="M:PowerForge.Tests.WebInheritanceImplementation.Execute(System.String)"><inheritdoc/></member>
                </members></doc>
                """);
            var xmlPaths = new[] { xml };
            if (separateContracts)
            {
                var document = System.Xml.Linq.XDocument.Load(xml);
                var contracts = document.Root!.Element("members")!.Elements("member")
                    .Where(member => !member.Attribute("name")!.Value.Contains("WebInheritanceImplementation", StringComparison.Ordinal)).ToArray();
                var contractPath = Path.Combine(root, "contracts.xml");
                new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("doc",
                    new System.Xml.Linq.XElement("members", contracts))).Save(contractPath);
                foreach (var contract in contracts) contract.Remove();
                document.Save(xml);
                xmlPaths = new[] { xml, contractPath };
            }
            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPaths = xmlPaths, AssemblyPath = typeof(WebInheritanceImplementation).Assembly.Location,
                OutputPath = output, Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Implicit inheritdoc", StringComparison.Ordinal));
            var path = Path.Combine(output, "types", "powerforge-tests-webinheritanceimplementation.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var serialized = json.RootElement.GetRawText();
            Assert.Contains("Run inherited summary.", serialized);
            Assert.Contains("Inherited input.", serialized);
            Assert.Contains("Inherited return.", serialized);
            Assert.Contains("Inherited name.", serialized);
            Assert.Contains("Inherited event.", serialized);
            Assert.Contains("Base execution.", serialized);
            var methods = json.RootElement.GetProperty("methods").EnumerateArray().ToArray();
            var run = methods.Single(method => method.GetProperty("name").GetString() == "Run");
            Assert.DoesNotContain("virtual", run.GetProperty("signature").GetString()!);
            Assert.Equal("value", run.GetProperty("parameters")[0].GetProperty("name").GetString());
            var execute = methods.Single(method => method.GetProperty("name").GetString() == "Execute");
            Assert.Contains("override", execute.GetProperty("signature").GetString()!);
            Assert.Contains("sealed", execute.GetProperty("signature").GetString()!);
        }
        finally { Directory.Delete(root, true); }
    }
}

public interface IWebInheritanceContract<T>
{
    T Run(T input);
    string Name { get; }
    event EventHandler Changed;
}

public class WebInheritanceBase
{
    public virtual void Execute(string input) { }
}

public sealed class WebInheritanceImplementation : WebInheritanceBase, IWebInheritanceContract<string>
{
    public string Run(string value) => value;
    public string Name => "Fixture";
    public event EventHandler Changed { add { } remove { } }
    public sealed override void Execute(string input) { }
}
