using PowerForge.Web;
using System.Text.Json;
using System.Xml.Linq;

public class WebApiDocsPowerShellRequiredExamplesTests
{
    [Fact]
    public void GeneratedExampleIncludesEveryRequiredArgument()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-required-example-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            XNamespace command = "http://schemas.microsoft.com/maml/dev/command/2004/10";
            XNamespace maml = "http://schemas.microsoft.com/maml/2004/10";
            var names = new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth" };
            XElement Parameter(string name) => new(command + "parameter", new XAttribute("required", "true"),
                new XElement(maml + "name", name),
                new XElement(maml + "description", new XElement(maml + "para", "Required value.")),
                new XElement(command + "parameterValue", new XAttribute("required", "true"), "String"));
            var helpPath = Path.Combine(root, "Sample-help.xml");
            new XDocument(new XElement("helpItems", new XAttribute("schema", "maml"),
                new XElement(command + "command",
                    new XElement(command + "details", new XElement(command + "name", "Invoke-Thing"),
                        new XElement(command + "commandType", "Cmdlet"),
                        new XElement(maml + "description", new XElement(maml + "para", "Runs with six required values."))),
                    new XElement(command + "syntax", new XElement(command + "syntaxItem",
                        new XAttribute("parameterSetName", "Default"), new XElement(command + "name", "Invoke-Thing"),
                        names.Select(Parameter))),
                    new XElement(command + "parameters", names.Select(Parameter))))).Save(helpPath);

            var output = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell, HelpPath = helpPath, OutputPath = output,
                Title = "PowerShell API", BaseUrl = "/api", Template = "docs", Format = "both"
            });

            Assert.Equal(1, result.TypeCount);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types", "invoke-thing.json")));
            var code = Assert.Single(json.RootElement.GetProperty("examples").EnumerateArray(),
                example => example.GetProperty("kind").GetString() == "code");
            Assert.Equal("Invoke-Thing " + string.Join(" ", names.Select(name => $"-{name} 'Value'")),
                code.GetProperty("text").GetString());
            Assert.Equal("GeneratedFallback", code.GetProperty("origin").GetString());
            Assert.Contains("-Sixth", File.ReadAllText(Path.Combine(output, "invoke-thing.html")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
