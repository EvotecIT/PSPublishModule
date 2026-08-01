namespace PowerForge.Tests;

public sealed class DocumentationMarkdownPossibleValueTests
{
    [Fact]
    public void MarkdownHelpWriter_RendersAnEmptyValidateSetAlternativeExplicitly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-empty-validateset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "EmptyValidateSetFixture",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Get-EmptyValidateSetFixture",
                        Parameters = new List<DocumentationParameterHelp>
                        {
                            new()
                            {
                                Name = "Value",
                                Type = "String",
                                PossibleValues = new List<string> { string.Empty, "''" }
                            }
                        }
                    }
                }
            };

            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, payload.ModuleName, root);
            var markdown = File.ReadAllText(Path.Combine(root, "Get-EmptyValidateSetFixture.md"));
            Assert.Contains("Possible values: '', ''''''", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
