using System;
using System.Collections.Generic;
using PowerForge;
using Xunit;

public class MarkdownHelpWriterTests
{
    private static string RenderCommandMarkdown(DocumentationCommandHelp command)
        => MarkdownHelpWriter.RenderCommandMarkdown(
            "DemoModule",
            command,
            exampleIndentClassifier: PowerShellMarkdownExampleIndentClassifier.Instance);

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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object {\r\n        \"}\"", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        Get-Process\r\n        }", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_IgnoresBlockCommentDelimitersWhenPreservingSingleBlockIndentation()
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
ForEach-Object { <# } #>
        New-Demo {
            Add-DemoStep
        }
        }
""",
                    Remarks = "Keeps authored block formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object { <# } #>\r\n        New-Demo {", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Add-DemoStep\r\n        }\r\n        }", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_IgnoresMultilineBlockCommentsWhenPreservingSingleBlockIndentation()
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
ForEach-Object { <#
        }
        #>
        Get-Process
        }
""",
                    Remarks = "Keeps authored block formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object { <#\r\n        }", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        #>\r\n        Get-Process\r\n        }", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $path = '.\\out.txt'\r\nSet-Content -Path $path -Value 'ok'", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentBeforeKeywordStatement()
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
            if ($image) {
                Add-DemoImage -Path $image
            }
""",
                    Remarks = "Adds an image when one is configured."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $image = '.\\Tests\\Assets\\CellImage.png'\r\nif ($image) {", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n    Add-DemoImage -Path $image\r\n}", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            if ($image)", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentBeforeNativeCommand()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Build-DemoProject",
            Synopsis = "Builds a demo project.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$root = '.\src'
            dotnet build $root
""",
                    Remarks = "Builds the project folder."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $root = '.\\src'\r\ndotnet build $root", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            dotnet", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $items = Get-ChildItem -Recurse\r\nSet-Content -Path .\\items.txt -Value $items.Count", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentEndingWithPathSeparator()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Save-DemoFiles",
            Synopsis = "Saves demo files.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$files = Get-ChildItem C:/Temp/
            Set-Content -Path .\files.txt -Value $files.Count
""",
                    Remarks = "Saves file counts."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $files = Get-ChildItem C:/Temp/\r\nSet-Content -Path .\\files.txt -Value $files.Count", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesInlineAssignmentEndingWithGlob()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Save-DemoFiles",
            Synopsis = "Saves demo files.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$files = Get-ChildItem C:/Temp/*
            Set-Content -Path .\files.txt -Value $files.Count
""",
                    Remarks = "Saves file counts."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $files = Get-ChildItem C:/Temp/*\r\nSet-Content -Path .\\files.txt -Value $files.Count", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> [string]$path = '.\\out.txt'\r\nSet-Content -Path $path -Value 'ok'", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesAttributedInlineAssignmentBeforeTopLevelCommand()
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
[Parameter(Mandatory=$true)]$path = '.\out.txt'
            Set-Content -Path $path -Value 'ok'
""",
                    Remarks = "Writes generated content."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> [Parameter(Mandatory=$true)]$path = '.\\out.txt'\r\nSet-Content -Path $path -Value 'ok'", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $prefix = '@\"'\r\nSet-Content -Path .\\prefix.txt -Value $prefix", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesExpressionOutputWithEqualsInString()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Show-DemoResponse",
            Synopsis = "Shows demo response.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$headers.Add('Accept=application/json')
        Status-Code 200
        $value Healthy
""",
                    Remarks = "Keeps authored output formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $headers.Add('Accept=application/json')\r\n        Status-Code 200", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        $value Healthy", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first -\r\n        $(", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Get-DemoValue\r\n        )", exampleSection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("/")]
    public void RenderCommandMarkdown_PreservesAdjacentArithmeticContinuationAfterInlinePrompt(string operatorToken)
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
                    Code = "$value = $first" + operatorToken + "\r\n        $second",
                    Remarks = "Keeps authored arithmetic continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first" + operatorToken + "\r\n        $second", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first + <# combine #>\r\n        $second", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_NormalizesAssignmentEndingWithPromptPunctuation()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Read-DemoAnswer",
            Synopsis = "Reads a demo answer.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$answer = Read-Host Continue?
            Set-Content -Path .\answer.txt -Value $answer
""",
                    Remarks = "Writes generated content."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $answer = Read-Host Continue?\r\nSet-Content -Path .\\answer.txt -Value $answer", exampleSection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n            Set-Content", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesAdjacentOperatorContinuationAfterInlinePrompt()
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
$value = $first+
        $second
""",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first+\r\n        $second", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesTabbedOperatorContinuationAfterInlinePrompt()
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
                    Code = "$ok = $value\t-eq\r\n        $(\r\n            Get-DemoValue\r\n        )",
                    Remarks = "Keeps authored continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = $value\t-eq\r\n        $(", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Get-DemoValue\r\n        )", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesMemberAccessContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-DemoName",
            Synopsis = "Gets a demo name.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$name = $object.
        $property
""",
                    Remarks = "Keeps authored member-access continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $name = $object.\r\n        $property", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesStaticMemberAccessContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-DemoName",
            Synopsis = "Gets a demo name.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$name = [Math]::
        $property
""",
                    Remarks = "Keeps authored member-access continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $name = [Math]::\r\n        $property", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesCompoundAssignmentContinuationAfterInlinePrompt()
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
$total +=
        $next
""",
                    Remarks = "Keeps authored compound assignment formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $total +=\r\n        $next", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesTernaryContinuationAfterInlinePrompt()
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
$result = $condition ?
        $trueValue :
        $falseValue
""",
                    Remarks = "Keeps authored ternary continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $result = $condition ?\r\n        $trueValue :", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        $falseValue", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesNullCoalescingContinuationAfterInlinePrompt()
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
$result = $value ??
        $fallback
""",
                    Remarks = "Keeps authored null-coalescing continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $result = $value ??\r\n        $fallback", exampleSection, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCommandMarkdown_PreservesPipelineChainContinuationAfterInlinePrompt()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Test-DemoPath",
            Synopsis = "Tests a demo path.",
            Examples = new List<DocumentationExampleHelp>
            {
                new()
                {
                    Introduction = "PS> ",
                    Code = """
$ok = (Test-Path $a) &&
        (Test-Path $b)
        New-Demo {
            Add-DemoStep
        }
""",
                    Remarks = "Keeps authored pipeline-chain continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = (Test-Path $a) &&\r\n        (Test-Path $b)", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n        New-Demo {\r\n            Add-DemoStep", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $ok = $value -eq\r\n        $(", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Get-DemoValue\r\n        )", exampleSection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("-ceq")]
    [InlineData("-shl")]
    public void RenderCommandMarkdown_PreservesAdditionalOperatorContinuationAfterInlinePrompt(string operatorToken)
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
                    Code = "$value = $first " + operatorToken + "\r\n        $second",
                    Remarks = "Keeps authored operator continuation formatting."
                }
            }
        };

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> $value = $first " + operatorToken + "\r\n        $second", exampleSection, StringComparison.Ordinal);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
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

        var markdown = RenderCommandMarkdown(command);
        var exampleSection = markdown.Substring(markdown.IndexOf("### EXAMPLE 1", StringComparison.Ordinal));

        Assert.Contains("PS> ForEach-Object { # filter\r\n        New-Demo {", exampleSection, StringComparison.Ordinal);
        Assert.Contains("\r\n            Status = Ready\r\n        }\r\n        }", exampleSection, StringComparison.Ordinal);
    }
}
