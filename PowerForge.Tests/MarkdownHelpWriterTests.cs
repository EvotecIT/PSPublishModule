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
    public void RenderCommandMarkdown_NormalizesInlineAssignmentBeforeNonBlockCommand()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoContent",
            Synopsis = "Sets demo content.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$path = '.\out.txt'
            Set-Content -Path $path -Value 'ok'
""",
                    Remarks = "Writes generated content."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $path = '.\\out.txt'\r\nSet-Content -Path $path -Value 'ok'", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentEndingWithSwitchParameter()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Save-DemoItems",
            Synopsis = "Saves demo items.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$items = Get-ChildItem -Recurse
            Set-Content -Path .\items.txt -Value $items.Count
""",
                    Remarks = "Saves item counts."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $items = Get-ChildItem -Recurse\r\nSet-Content -Path .\\items.txt -Value $items.Count", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesTypedInlineAssignmentBeforeTopLevelCommand()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoContent",
            Synopsis = "Sets demo content.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
[string]$path = '.\out.txt'
            Set-Content -Path $path -Value 'ok'
""",
                    Remarks = "Writes generated content."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> [string]$path = '.\\out.txt'\r\nSet-Content -Path $path -Value 'ok'", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentWithQuotedHereStringMarker()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoPrefix",
            Synopsis = "Sets a demo prefix.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$prefix = '@"'
            Set-Content -Path .\prefix.txt -Value $prefix
""",
                    Remarks = "Writes generated content."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $prefix = '@\"'\r\nSet-Content -Path .\\prefix.txt -Value $prefix", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
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
    public void RenderCommandMarkdown_PreservesArithmeticContinuationAfterInlinePrompt()
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
$value = $first -
        $(
            Get-DemoValue
        )
""",
                    Remarks = "Keeps authored arithmetic continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first -\r\n        $(", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Get-DemoValue\r\n        )", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesUnmatchedDelimiterContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-DemoItems",
            Synopsis = "Sets demo items.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$items = @(Get-Demo
        Where-Object Status -eq 'Ready')
""",
                    Remarks = "Keeps authored grouping continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $items = @(Get-Demo\r\n        Where-Object Status -eq 'Ready')", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesOrdinaryMultilineStringAssignment()
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
$body = "
        Get-Process
"
""",
                    Remarks = "Keeps authored string content."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $body = \"\r\n        Get-Process\r\n\"", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesOperatorContinuationBeforeBlockComment()
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
$value = $first + <# combine #>
        $second
""",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first + <# combine #>\r\n        $second", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesUnaryNegationContinuationAfterInlinePrompt()
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
$ok = !
        $config.Disabled
""",
                    Remarks = "Keeps authored unary continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = !\r\n        $config.Disabled", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesNotOperatorContinuationAfterInlinePrompt()
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
$ok = -not
        $config.Disabled
""",
                    Remarks = "Keeps authored unary continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = -not\r\n        $config.Disabled", exampleSection, StringComparison.Ordinal);
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

    [Fact]
    public void RenderCommandMarkdown_PreservesBlockShapedOutputAfterCommand()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Show-DemoTemplate",
            Synopsis = "Shows a demo template.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
Show-DemoTemplate
        $config = @{
            Name = 'Demo'
        }
""",
                    Remarks = "Keeps authored template output formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> Show-DemoTemplate\r\n        $config = @{", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Name = 'Demo'\r\n        }", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesOperatorContinuationBeforeLineComment()
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
$value = $first + # combine
        $second
        New-DemoDeck {
            Add-DemoImage
        }
""",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first + # combine\r\n        $second", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        New-DemoDeck {\r\n            Add-DemoImage\r\n        }", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesSingleBlockWithTrailingComment()
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
ForEach-Object { # filter
        New-Demo {
            Status = Ready
        }
        }
""",
                    Remarks = "Keeps authored block formatting."
                }
            }
        };

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object { # filter\r\n        New-Demo {", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Status = Ready\r\n        }\r\n        }", exampleSection, StringComparison.Ordinal);
    }
}
