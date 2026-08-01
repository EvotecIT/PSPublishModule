using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class DocumentationCommandIdentityCompatibilityTests
{
    [Fact]
    public void DocumentationNormalizer_RewritesEncodedParameterNamesWithoutPrefixCollisions()
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
                        new DocumentationParameterHelp { Name = invalid },
                        new DocumentationParameterHelp { Name = "P%u0001" }
                    }
                }
            }
        };

        DocumentationMetadataNormalizer.Normalize(payload);

        Assert.Equal(
            "Get-Test [-P%u0001 [encoded 1] <string>] [-P%u0001 <string>] [-P([char]1)Suffix <string>]",
            Assert.Single(payload.Commands[0].Syntax).Text);
    }

    [Fact]
    public void DocumentationNormalizer_PreservesXmlValidBindableIdentitiesInGeneratedArtifacts()
    {
        Assert.Equal(
            "Get-A%uD800",
            DocumentationIdentityTextFormatter.PreserveBindable("Get-A%uD800", "Command name"));
        Assert.Equal(
            "Get-A%25uD800",
            DocumentationIdentityTextFormatter.Format("Get-A%uD800"));

        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "CommandIdentityFixture",
            Commands =
            [
                new DocumentationCommandHelp { Name = "Get-A%uD800", CommandType = "Function" },
                new DocumentationCommandHelp { Name = "Get-A%25uD800", CommandType = "Function" }
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

    [Fact]
    public void DocumentationNormalizer_RejectsInvalidCommandAndRendersOtherIdentitiesInjectively()
    {
        var invalid = new string('\uD800', 1);
        AssertInvalid(
            new DocumentationCommandHelp { Name = "Get-A" + invalid, CommandType = "Function" },
            "Command name contains XML-invalid characters");

        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "InvalidIdentityFixture",
            Commands =
            [
                new DocumentationCommandHelp
                {
                    Name = "Get-Valid",
                    CommandType = "Function",
                    Syntax =
                    [
                        new DocumentationSyntaxHelp
                        {
                            Name = "Default",
                            Text = "Get-Valid [-P" + invalid + " <string>] [-P%uD800 <string>] [-P([char]55296) <string>]"
                        }
                    ],
                    Parameters =
                    [
                        new DocumentationParameterHelp
                        {
                            Name = "P" + invalid,
                            Aliases = ["Alias" + invalid, "Alias([char]55296)"]
                        },
                        new DocumentationParameterHelp { Name = "P%uD800" },
                        new DocumentationParameterHelp { Name = "P([char]55296)" }
                    ]
                }
            ]
        };
        DocumentationMetadataNormalizer.Normalize(payload);
        Assert.Equal("P%uD800 [encoded 1]", payload.Commands[0].Parameters[0].Name);
        Assert.Equal("P%uD800", payload.Commands[0].Parameters[1].Name);
        Assert.Equal("P([char]55296)", payload.Commands[0].Parameters[2].Name);
        Assert.Equal("(-join @('Alias', ([char]55296)))", payload.Commands[0].Parameters[0].Aliases[0]);
        Assert.Equal("Alias([char]55296)", payload.Commands[0].Parameters[0].Aliases[1]);
        Assert.Equal(
            "Get-Valid [-P%uD800 [encoded 1] <string>] [-P%uD800 <string>] [-P([char]55296) <string>]",
            payload.Commands[0].Syntax[0].Text);

        var root = Path.Combine(Path.GetTempPath(), "pf-doc-syntax-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, payload.ModuleName, root);
            var markdown = File.ReadAllText(Path.Combine(root, "Get-Valid.md"));
            Assert.Contains(
                "Get-Valid [-P%uD800 [encoded 1] <string>] [-P%uD800 <string>] [-P([char]55296) <string>]",
                markdown,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static void AssertInvalid(DocumentationCommandHelp command, string expected)
        {
            var invalidPayload = new DocumentationExtractionPayload
            {
                ModuleName = "InvalidIdentityFixture",
                Commands = [command]
            };
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DocumentationMetadataNormalizer.Normalize(invalidPayload));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocumentationNormalizer_RendersParameterSetIdentitiesInjectivelyAndPreservesBoundaries()
    {
        var invalid = "S" + new string('\uD800', 1);
        var literal = "S%uD800";
        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "ParameterSetIdentityFixture",
            Commands =
            [
                new DocumentationCommandHelp
                {
                    Name = "Get-Test",
                    CommandType = "Function",
                    DefaultParameterSet = invalid,
                    Syntax =
                    [
                        new DocumentationSyntaxHelp { Name = invalid, Text = "Get-Test -Value <string>" },
                        new DocumentationSyntaxHelp { Name = literal, Text = "Get-Test -Value <string>" },
                        new DocumentationSyntaxHelp { Name = " A ", Text = "Get-Test -Value <string>" },
                        new DocumentationSyntaxHelp { Name = "A", Text = "Get-Test -Value <string>" },
                        new DocumentationSyntaxHelp { Name = "' A '", Text = "Get-Test -Value <string>" }
                    ],
                    Parameters =
                    [
                        new DocumentationParameterHelp
                        {
                            Name = "Value",
                            Type = "String",
                            ParameterSets = [invalid, literal, " A ", "A", "' A '"],
                            ParameterSetRequired = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                            {
                                [invalid] = true,
                                [literal] = false,
                                [" A "] = true,
                                ["A"] = false,
                                ["' A '"] = false
                            }
                        }
                    ]
                }
            ]
        };

        DocumentationMetadataNormalizer.Normalize(payload);
        var command = payload.Commands[0];
        Assert.Equal("S%uD800 [encoded 1]", command.DefaultParameterSet);
        Assert.Equal(
            ["S%uD800 [encoded 1]", "S%uD800", " A ", "A", "' A '"],
            command.Syntax.Select(item => item.Name).ToArray());
        Assert.Equal(
            ["S%uD800 [encoded 1]", "S%uD800", " A ", "A", "' A '"],
            command.Parameters[0].ParameterSets);
        Assert.Equal(5, command.Parameters[0].ParameterSetRequired.Count);

        var root = Path.Combine(Path.GetTempPath(), "pf-doc-parameter-set-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, payload.ModuleName, root);
            Assert.Contains(
                "Parameter Sets: S%uD800 [encoded 1], S%uD800, ' A ' [encoded 1], A, ' A '",
                File.ReadAllText(Path.Combine(root, "Get-Test.md")),
                StringComparison.Ordinal);
            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, payload.ModuleName, root);
            var setNames = XDocument.Load(mamlPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "syntaxItem")
                .Select(element => element.Attribute("parameterSetName"))
                .Where(attribute => attribute is not null)
                .Select(attribute => attribute!.Value)
                .ToArray();
            Assert.Equal(
                ["S%uD800 [encoded 1]", "S%uD800", " A ", "A", "' A '"],
                setNames);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
