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
            ],
            RuntimeInputs =
            [
                new DocumentationTypeHelp { Name = "String[]", ClrTypeName = "System.String[]" },
                new DocumentationTypeHelp
                {
                    Name = "ManagedModuleVersionInfo[]",
                    ClrTypeName = "PowerForge.ManagedModuleVersionInfo[]"
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
    public void Normalize_PreservesAuthoredInputDescriptionEmbeddedByPowerShell()
    {
        var aggregate = "System.String\r\nThis is string input.";
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Demo",
            Inputs =
            [
                new DocumentationTypeHelp
                {
                    Name = aggregate,
                    ClrTypeName = aggregate
                }
            ],
            RuntimeInputs =
            [
                new DocumentationTypeHelp { Name = "String", ClrTypeName = "System.String" }
            ]
        };

        DocumentationMetadataNormalizer.Normalize(new DocumentationExtractionPayload { Commands = [command] });

        var input = Assert.Single(command.Inputs);
        Assert.Equal("System.String", input.Name);
        Assert.Equal("System.String", input.ClrTypeName);
        Assert.Equal("This is string input.", input.Description);
        Assert.Empty(command.RuntimeInputs);

        var markdown = MarkdownHelpWriter.RenderCommandMarkdown("DemoModule", command);
        Assert.Contains("- `System.String`: This is string input.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_UnwrapsNullablePipelineInputIdentity()
    {
        const string runtimeType = "System.Nullable`1[[VisibilityMode, Demo]][]";
        var command = new DocumentationCommandHelp
        {
            Inputs = [new DocumentationTypeHelp { Name = runtimeType, ClrTypeName = runtimeType }],
            RuntimeInputs =
            [
                new DocumentationTypeHelp
                {
                    Name = "Nullable`1[]",
                    ClrTypeName = runtimeType
                }
            ],
            Parameters =
            [
                new DocumentationParameterHelp
                {
                    Name = "Modes",
                    Type = "Nullable`1[]",
                    NullableUnderlyingTypeName = "VisibilityMode",
                    NullableArrayRanks = [1],
                    PipelineInput = "True (ByValue)"
                }
            ]
        };

        DocumentationMetadataNormalizer.Normalize(new DocumentationExtractionPayload { Commands = [command] });

        var input = Assert.Single(command.Inputs);
        Assert.Equal("VisibilityMode[]", input.Name);
        Assert.Equal("VisibilityMode[]", input.ClrTypeName);
        Assert.DoesNotContain("Nullable", MarkdownHelpWriter.RenderCommandMarkdown("Demo", command));
    }
}
