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

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentBeforeTopLevelCommand()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Add-DemoImage",
            Synopsis = "Adds a demo image.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$image = '.\Tests\Assets\CellImage.png'
            New-DemoDeck -Path .\Examples\Documents\DemoImage.pptx {
                Add-DemoImage -Path $image
            }
""",
                    Remarks = "Adds an image to a generated slide."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $image = '.\\Tests\\Assets\\CellImage.png'\r\nNew-DemoDeck -Path .\\Examples\\Documents\\DemoImage.pptx", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n    Add-DemoImage -Path $image\r\n}", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            New-DemoDeck", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesIndentedPipelineContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Demo",
            Synopsis = "Gets demo rows.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
Get-Demo |
        Where-Object Status -eq 'Ready' |
        Select-Object Name
""",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> Get-Demo |\r\n        Where-Object Status -eq 'Ready' |", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        Select-Object Name", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesCommandLikeOutputAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Show-DemoStatus",
            Synopsis = "Shows demo status.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
Show-DemoStatus
        Service-Status Ready
        $value Healthy
""",
                    Remarks = "Keeps authored output formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> Show-DemoStatus\r\n        Service-Status Ready", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        $value Healthy", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesHereStringAfterIncompleteAssignment()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoBody",
            Synopsis = "Sets a demo body.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$body =
        @"
        Get-Process
        "@
""",
                    Remarks = "Keeps authored here-string content."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $body =\r\n        @\"", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        Get-Process\r\n        \"@", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesOperatorContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoValue",
            Synopsis = "Sets a demo value.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$value = $first +
        $second
""",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first +\r\n        $second", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesComparisonOperatorContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Test-DemoValue",
            Synopsis = "Tests a demo value.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$ok = $value -eq
        $(
            Get-DemoValue
        )
""",
                    Remarks = "Keeps authored comparison continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = $value -eq\r\n        $(", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Get-DemoValue\r\n        )", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesLabeledOutputBeforeIndentedCommandLikeText()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Show-DemoResult",
            Synopsis = "Shows a demo result.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
Result:
        New-Demo {
            Status = Ready
        }
""",
                    Remarks = "Keeps authored labeled output formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> Result:\r\n        New-Demo {", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Status = Ready\r\n        }", exampleSection, StringComparison.Ordinal);
    }
}
