using System;
using System.Collections.Generic;
using PowerForge;
using Xunit;

public class MarkdownHelpWriterTests
{
    [Fact]
    public void RenderCommandMarkdown_NormalizesIncidentalExampleIndentation()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Add-DemoDeck",
            Synopsis = "Adds a demo deck.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$plan = New-DemoDeckPlan {
            Add-DemoSection -Title 'Service Review'
            Add-DemoCardGrid -Cards @(
                @{ Title = 'Availability'; Items = @('Healthy', 'No critical incidents') }
                @{ Title = 'Risk'; Items = @('One dependency on watch') }
            )
        }
        New-DemoDeck -Path .\Examples\Documents\DesignerDeck.pptx {
            Add-DemoDeck -Plan $plan
        }
""",
                    Remarks = "Uses the current deck plan."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $plan = New-DemoDeckPlan {\r\n    Add-DemoSection -Title 'Service Review'", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\nNew-DemoDeck -Path .\\Examples\\Documents\\DesignerDeck.pptx {\r\n    Add-DemoDeck -Plan $plan\r\n}", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Add-DemoSection", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n        New-DemoDeck -Path", exampleSection, StringComparison.Ordinal);
    }
}
