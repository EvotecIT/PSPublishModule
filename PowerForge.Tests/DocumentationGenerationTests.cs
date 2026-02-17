using PowerForge;
using System.Xml.Linq;

public class DocumentationGenerationTests
{
    [Fact]
    public void DocumentationFallbackEnricher_GeneratesExamplesFromSyntax()
    {
        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "TestModule",
            Commands = new List<DocumentationCommandHelp>
            {
                new()
                {
                    Name = "Invoke-Thing",
                    CommandType = "Cmdlet",
                    Synopsis = "Does a thing.",
                    Syntax = new List<DocumentationSyntaxHelp>
                    {
                        new()
                        {
                            Name = "ByName",
                            IsDefault = true,
                            Text = "Invoke-Thing -Name <String> [-Force] [<CommonParameters>]"
                        },
                        new()
                        {
                            Name = "ById",
                            IsDefault = false,
                            Text = "Invoke-Thing -Id <Int32> [-Force] [<CommonParameters>]"
                        }
                    },
                    Parameters = new List<DocumentationParameterHelp>
                    {
                        new() { Name = "Name", Type = "String" },
                        new() { Name = "Id", Type = "Int32" },
                        new() { Name = "Force", Type = "SwitchParameter" }
                    },
                    Examples = new List<DocumentationExampleHelp>()
                }
            }
        };

        DocumentationFallbackEnricher.Enrich(payload, new NullLogger());

        var cmd = payload.Commands.Single();
        Assert.NotNull(cmd.Examples);
        Assert.True(cmd.Examples.Count >= 2);
        Assert.Contains("-Name 'Name'", cmd.Examples[0].Code);
        Assert.Contains("-Id 1", cmd.Examples[1].Code);
    }

    [Fact]
    public void AboutTopicMarkdown_ConvertsHelpTxtToMarkdown()
    {
        const string content = @"
TOPIC
    about_MyTopic

SHORT DESCRIPTION
    One-line summary.

LONG DESCRIPTION
    First paragraph.

    Second paragraph.

EXAMPLES
    PS> Invoke-Something
";

        var res = AboutTopicMarkdown.Convert("about_MyTopic.help", content);

        Assert.Equal("about_MyTopic", res.TopicName);
        Assert.Contains("# about_MyTopic", res.Markdown);
        Assert.Contains("## Short Description", res.Markdown);
        Assert.Contains("One-line summary.", res.Markdown);
        Assert.Contains("## Long Description", res.Markdown);
        Assert.Contains("First paragraph.", res.Markdown);
        Assert.Contains("Second paragraph.", res.Markdown);
        Assert.Contains("## Examples", res.Markdown);
    }

    [Fact]
    public void MamlHelpWriter_WritesParameterSetNameAndPossibleValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-maml-help-writer-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "TestModule",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Thing",
                        CommandType = "Cmdlet",
                        Synopsis = "Does a thing.",
                        Description = "Does a thing.",
                        Syntax = new List<DocumentationSyntaxHelp>
                        {
                            new() { Name = "ByMode", IsDefault = true, Text = "Invoke-Thing -Mode <String>" }
                        },
                        Parameters = new List<DocumentationParameterHelp>
                        {
                            new()
                            {
                                Name = "Mode",
                                Type = "String",
                                ParameterSets = new List<string> { "ByMode" },
                                PossibleValues = new List<string> { "Basic", "Advanced" }
                            }
                        }
                    }
                }
            };

            var writer = new MamlHelpWriter();
            var path = writer.WriteExternalHelpFile(payload, "TestModule", root);
            Assert.True(File.Exists(path));

            var doc = XDocument.Load(path);
            XNamespace commandNs = "http://schemas.microsoft.com/maml/dev/command/2004/10";
            XNamespace mamlNs = "http://schemas.microsoft.com/maml/2004/10";

            var syntaxItem = doc.Descendants(commandNs + "syntaxItem").FirstOrDefault();
            Assert.NotNull(syntaxItem);
            Assert.Equal("ByMode", syntaxItem!.Attribute("parameterSetName")?.Value);

            var parameter = doc.Descendants(commandNs + "parameter")
                .FirstOrDefault(p => string.Equals(p.Element(mamlNs + "name")?.Value, "Mode", StringComparison.Ordinal));
            Assert.NotNull(parameter);

            var possibleValues = parameter!
                .Element(commandNs + "parameterValueGroup")?
                .Elements(commandNs + "parameterValue")
                .Select(v => v.Value)
                .ToArray();
            Assert.NotNull(possibleValues);
            Assert.Contains("Basic", possibleValues!);
            Assert.Contains("Advanced", possibleValues!);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
