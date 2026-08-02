using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class DocumentationInputTypeNormalizationTests
{
    [Fact]
    public void Normalize_SplitsPowerShellAggregatedInputTypesForGeneratedHelp()
    {
        var aggregate = "System.String[]\r\nPowerForge.ManagedModuleVersionInfo[]\r\n";
        var command = new DocumentationCommandHelp
        {
            Name = "Install-ManagedModule",
            Inputs =
            [
                new DocumentationTypeHelp
                {
                    Name = aggregate,
                    ClrTypeName = aggregate
                }
            ]
        };
        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "PSPublishModule",
            Commands = [command]
        };

        DocumentationMetadataNormalizer.Normalize(payload);

        Assert.Collection(
            command.Inputs,
            input =>
            {
                Assert.Equal("System.String[]", input.Name);
                Assert.Equal("System.String[]", input.ClrTypeName);
            },
            input =>
            {
                Assert.Equal("PowerForge.ManagedModuleVersionInfo[]", input.Name);
                Assert.Equal("PowerForge.ManagedModuleVersionInfo[]", input.ClrTypeName);
            });

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("PSPublishModule", command);
        Assert.Contains(
            "## INPUTS\r\n\r\n- `System.String[]`\r\n- `PowerForge.ManagedModuleVersionInfo[]`\r\n",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("%u000D", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("%u000A", markdown, StringComparison.Ordinal);

        var root = Path.Combine(Path.GetTempPath(), "pf-input-types-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, "PSPublishModule", root);
            var document = XDocument.Load(mamlPath);
            var inputNames = document.Descendants()
                .Where(element => element.Name.LocalName == "inputType")
                .Select(element => element.Descendants().First(child => child.Name.LocalName == "name").Value)
                .ToArray();

            Assert.Equal(
                new[] { "System.String[]", "PowerForge.ManagedModuleVersionInfo[]" },
                inputNames);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Normalize_DoesNotSplitAnAuthoredInputDescription()
    {
        var aggregate = "First\r\nSecond";
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Demo",
            Inputs =
            [
                new DocumentationTypeHelp
                {
                    Name = aggregate,
                    ClrTypeName = aggregate,
                    Description = "An explicitly authored input identity."
                }
            ]
        };

        DocumentationMetadataNormalizer.Normalize(new DocumentationExtractionPayload { Commands = [command] });

        var input = Assert.Single(command.Inputs);
        Assert.Equal(aggregate, input.Name);
        Assert.Equal("An explicitly authored input identity.", input.Description);

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        Assert.Contains("- `First%u000D%u000ASecond`", markdown, StringComparison.Ordinal);
    }
}
