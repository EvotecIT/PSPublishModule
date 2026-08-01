using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class DocumentationCommandIdentityCompatibilityTests
{
    [Fact]
    public void SyntaxIdentityNormalizer_RewritesEncodedParameterNames()
    {
        var invalid = "P\u0001";
        var payload = new DocumentationExtractionPayload
        {
            Commands =
            {
                new DocumentationCommandHelp
                {
                    Name = "Get-Test",
                    Syntax = { new DocumentationSyntaxHelp { Text = $"Get-Test [-{invalid} <string>] [-P%u0001 <string>] [-{invalid}Suffix <string>]" } },
                    Parameters =
                    {
                        new DocumentationParameterHelp { OriginalName = invalid, Name = "P%u0001 [encoded 1]" },
                        new DocumentationParameterHelp { OriginalName = "P%u0001", Name = "P%u0001" }
                    }
                }
            }
        };

        DocumentationSyntaxIdentityNormalizer.Normalize(payload);

        Assert.Equal(
            $"Get-Test [-P%u0001 [encoded 1] <string>] [-P%u0001 <string>] [-{invalid}Suffix <string>]",
            Assert.Single(payload.Commands[0].Syntax).Text);
    }

    [Fact]
    public void DocumentationNormalizer_UsesCollisionFreeCommandIdentifiersInGeneratedArtifacts()
    {
        var invalidName = "Get-A" + '\uD800';
        var literalName = "Get-A%uD800";
        Assert.Equal("Get-A%uD800", DocumentationIdentityTextFormatter.Format(invalidName));
        Assert.Equal("Get-A%25uD800", DocumentationIdentityTextFormatter.Format(literalName));

        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "CommandIdentityFixture",
            Commands =
            [
                new DocumentationCommandHelp { Name = invalidName, CommandType = "Function" },
                new DocumentationCommandHelp { Name = literalName, CommandType = "Function" }
            ]
        };
        DocumentationMetadataNormalizer.Normalize(payload);
        Assert.Equal(
            ["Get-A%uD800", "Get-A%25uD800"],
            payload.Commands.Select(command => command.Name).ToArray());

        var root = Path.Combine(Path.GetTempPath(), "pf-doc-command-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var docsPath = Path.Combine(root, "Docs");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "CommandIdentityFixture", docsPath);
            Assert.True(File.Exists(Path.Combine(docsPath, "Get-A%uD800.md")));
            Assert.True(File.Exists(Path.Combine(docsPath, "Get-A%25uD800.md")));

            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                payload,
                "CommandIdentityFixture",
                root);
            var names = XDocument.Load(mamlPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "details")
                .SelectMany(element => element.Descendants())
                .Where(element => element.Name.LocalName == "name")
                .Select(element => element.Value)
                .ToArray();
            Assert.Contains("Get-A%uD800", names);
            Assert.Contains("Get-A%25uD800", names);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
