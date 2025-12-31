using PowerForge;

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
}
