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

    [Fact]
    public void RenderCommandMarkdown_PreservesAuthoredIndentationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Show-DemoOutput",
            Synopsis = "Shows demo output.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
Show-DemoOutput
        Name        Status
        Service A   Healthy
        Service B   Watching
""",
                    Remarks = "Shows aligned console output."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> Show-DemoOutput\r\n        Name        Status", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        Service A   Healthy\r\n        Service B   Watching", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesSingleBlockBodyIndentationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Invoke-DemoBlock",
            Synopsis = "Invokes a demo block.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
ForEach-Object {
        $_.Name
        $_.Status
        }
""",
                    Remarks = "Keeps authored block formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object {\r\n        $_.Name", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        $_.Status\r\n        }", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_IgnoresQuotedDelimitersWhenPreservingSingleBlockIndentation()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Invoke-DemoBlock",
            Synopsis = "Invokes a demo block.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
ForEach-Object {
        "}"
        Get-Process
        }
""",
                    Remarks = "Keeps authored block formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object {\r\n        \"}\"", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        Get-Process\r\n        }", exampleSection, StringComparison.Ordinal);
    }
}
