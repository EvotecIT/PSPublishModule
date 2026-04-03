using PowerForge;
using System.Xml.Linq;

public class DocumentationGenerationTests
{
    [Fact]
    public void DocumentationFallbackEnricher_GeneratesExamplesFromSyntax()
    {
        var payload = new DocumentationExtractionPayload
        {
            ModuleName = "TestModule",
            Commands = new List<DocumentationCommandHelp>
            {
                new()
                {
                    Name = "Invoke-Thing",
                    CommandType = "Cmdlet",
                    Synopsis = "Does a thing.",
                    Syntax = new List<DocumentationSyntaxHelp>
                    {
                        new()
                        {
                            Name = "ByName",
                            IsDefault = true,
                            Text = "Invoke-Thing -Name <String> [-Force] [<CommonParameters>]"
                        },
                        new()
                        {
                            Name = "ById",
                            IsDefault = false,
                            Text = "Invoke-Thing -Id <Int32> [-Force] [<CommonParameters>]"
                        }
                    },
                    Parameters = new List<DocumentationParameterHelp>
                    {
                        new() { Name = "Name", Type = "String" },
                        new() { Name = "Id", Type = "Int32" },
                        new() { Name = "Force", Type = "SwitchParameter" }
                    },
                    Examples = new List<DocumentationExampleHelp>()
                }
            }
        };

        DocumentationFallbackEnricher.Enrich(payload, new NullLogger());

        var cmd = payload.Commands.Single();
        Assert.NotNull(cmd.Examples);
        Assert.True(cmd.Examples.Count >= 2);
        Assert.Contains("-Name 'Name'", cmd.Examples[0].Code);
        Assert.Contains("-Id 1", cmd.Examples[1].Code);
    }

    [Fact]
    public void AboutTopicMarkdown_ConvertsHelpTxtToMarkdown()
    {
        const string content = @"
TOPIC
    about_MyTopic

SHORT DESCRIPTION
    One-line summary.

LONG DESCRIPTION
    First paragraph.

    Second paragraph.

EXAMPLES
    PS> Invoke-Something
";

        var res = AboutTopicMarkdown.Convert("about_MyTopic.help", content);

        Assert.Equal("about_MyTopic", res.TopicName);
        Assert.Contains("# about_MyTopic", res.Markdown);
        Assert.Contains("## Short Description", res.Markdown);
        Assert.Contains("One-line summary.", res.Markdown);
        Assert.Contains("## Long Description", res.Markdown);
        Assert.Contains("First paragraph.", res.Markdown);
        Assert.Contains("Second paragraph.", res.Markdown);
        Assert.Contains("## Examples", res.Markdown);
    }

    [Fact]
    public void MamlHelpWriter_WritesParameterSetNameAndPossibleValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-maml-help-writer-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "TestModule",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Thing",
                        CommandType = "Cmdlet",
                        Synopsis = "Does a thing.",
                        Description = "Does a thing.",
                        Syntax = new List<DocumentationSyntaxHelp>
                        {
                            new() { Name = "ByMode", IsDefault = true, Text = "Invoke-Thing -Mode <String>" }
                        },
                        Parameters = new List<DocumentationParameterHelp>
                        {
                            new()
                            {
                                Name = "Mode",
                                Type = "String",
                                ParameterSets = new List<string> { "ByMode" },
                                PossibleValues = new List<string> { "Basic", "Advanced" }
                            }
                        }
                    }
                }
            };

            var writer = new MamlHelpWriter();
            var path = writer.WriteExternalHelpFile(payload, "TestModule", root);
            Assert.True(File.Exists(path));
            Assert.False(HasIsolatedLf(File.ReadAllText(path)), "External help XML should be normalized to CRLF line endings.");

            var doc = XDocument.Load(path);
            XNamespace commandNs = "http://schemas.microsoft.com/maml/dev/command/2004/10";
            XNamespace mamlNs = "http://schemas.microsoft.com/maml/2004/10";

            var syntaxItem = doc.Descendants(commandNs + "syntaxItem").FirstOrDefault();
            Assert.NotNull(syntaxItem);
            Assert.Equal("ByMode", syntaxItem!.Attribute("parameterSetName")?.Value);

            var parameter = doc.Descendants(commandNs + "parameter")
                .FirstOrDefault(p => string.Equals(p.Element(mamlNs + "name")?.Value, "Mode", StringComparison.Ordinal));
            Assert.NotNull(parameter);

            var possibleValues = parameter!
                .Element(commandNs + "parameterValueGroup")?
                .Elements(commandNs + "parameterValue")
                .Select(v => v.Value)
                .ToArray();
            Assert.NotNull(possibleValues);
            Assert.Contains("Basic", possibleValues!);
            Assert.Contains("Advanced", possibleValues!);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AboutTopicWriter_PrefersHelpTxt_AndGeneratesIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-about-writer-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "about_Demo.help.txt"), """
TOPIC
    about_Demo

SHORT DESCRIPTION
    Preferred help topic description.
""");

            File.WriteAllText(Path.Combine(root, "about_Demo.txt"), """
TOPIC
    about_Demo

SHORT DESCRIPTION
    Secondary text topic description.
""");

            File.WriteAllText(Path.Combine(root, "about_Extra.help.txt"), """
TOPIC
    about_Extra

SHORT DESCRIPTION
    Extra topic description.
""");

            var result = new AboutTopicWriter().Write(root, docs);

            Assert.Equal(2, result.Topics.Length);

            var demoMd = Path.Combine(docs, "About", "about_Demo.md");
            Assert.True(File.Exists(demoMd));
            var demoText = File.ReadAllText(demoMd);
            Assert.Contains("Preferred help topic description.", demoText);
            Assert.DoesNotContain("Secondary text topic description.", demoText);

            var aboutReadme = Path.Combine(docs, "About", "README.md");
            Assert.True(File.Exists(aboutReadme));
            var index = File.ReadAllText(aboutReadme);
            Assert.Contains("[about_Demo](about_Demo.md)", index);
            Assert.Contains("[about_Extra](about_Extra.md)", index);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AboutTopicTemplateGenerator_WritesCanonicalTemplateFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-about-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = AboutTopicTemplateGenerator.WriteTemplateFile(
                outputDirectory: root,
                topicName: "Troubleshooting",
                force: false,
                shortDescription: "How to troubleshoot common issues.");

            Assert.True(File.Exists(output));
            Assert.EndsWith("about_Troubleshooting.help.txt", output, StringComparison.OrdinalIgnoreCase);

            var text = File.ReadAllText(output);
            Assert.Contains("TOPIC", text);
            Assert.Contains("about_Troubleshooting", text);
            Assert.Contains("How to troubleshoot common issues.", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AboutTopicTemplateGenerator_WritesMarkdownTemplateFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-about-template-md-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = AboutTopicTemplateGenerator.WriteTemplateFile(
                outputDirectory: root,
                topicName: "Troubleshooting",
                force: false,
                shortDescription: "Markdown troubleshooting summary.",
                format: AboutTopicTemplateFormat.Markdown);

            Assert.True(File.Exists(output));
            Assert.EndsWith("about_Troubleshooting.md", output, StringComparison.OrdinalIgnoreCase);

            var text = File.ReadAllText(output);
            Assert.Contains("topic: about_Troubleshooting", text);
            Assert.Contains("# about_Troubleshooting", text);
            Assert.Contains("Markdown troubleshooting summary.", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AboutTopicWriter_UsesAdditionalSourcePaths_RelativeToStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-about-extra-paths-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, "staging");
        var extra = Path.Combine(root, "Help", "About");
        var docs = Path.Combine(root, "Docs");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(extra);

        try
        {
            File.WriteAllText(Path.Combine(extra, "about_Custom.help.txt"), """
TOPIC
    about_Custom

SHORT DESCRIPTION
    Custom path topic.
""");

            var result = new AboutTopicWriter().Write(staging, docs, new[] { @"..\Help\About" });
            Assert.Single(result.Topics);

            var customMd = Path.Combine(docs, "About", "about_Custom.md");
            Assert.True(File.Exists(customMd));
            Assert.Contains("Custom path topic.", File.ReadAllText(customMd));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AboutTopicWriter_SupportsMarkdownSources_AndPriorityRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-about-markdown-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "about_Priority.txt"), """
TOPIC
    about_Priority

SHORT DESCRIPTION
    Plain text source.
""");

            File.WriteAllText(Path.Combine(root, "about_Priority.md"), """
# about_Priority

Markdown source.
""");

            File.WriteAllText(Path.Combine(root, "about_Priority.help.txt"), """
TOPIC
    about_Priority

SHORT DESCRIPTION
    Help text source.
""");

            File.WriteAllText(Path.Combine(root, "about_MarkdownOnly.md"), """
# about_MarkdownOnly

Markdown only topic body.
""");

            var result = new AboutTopicWriter().Write(root, docs);
            Assert.Equal(2, result.Topics.Length);

            var priorityPath = Path.Combine(docs, "About", "about_Priority.md");
            var priorityText = File.ReadAllText(priorityPath);
            Assert.Contains("Help text source.", priorityText);
            Assert.DoesNotContain("Markdown source.", priorityText);
            Assert.DoesNotContain("Plain text source.", priorityText);

            var markdownOnlyPath = Path.Combine(docs, "About", "about_MarkdownOnly.md");
            var markdownOnlyText = File.ReadAllText(markdownOnlyPath);
            Assert.Contains("topic: about_MarkdownOnly", markdownOnlyText);
            Assert.Contains("Markdown only topic body.", markdownOnlyText);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MarkdownHelpWriter_Readme_IgnoresAboutReadmeIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-markdown-readme-about-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "Docs");
        var about = Path.Combine(docs, "About");
        Directory.CreateDirectory(about);

        try
        {
            File.WriteAllText(Path.Combine(about, "README.md"), "# About Topics");
            File.WriteAllText(Path.Combine(about, "about_Custom.md"), "# about_Custom");

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "DemoModule",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Get-Demo",
                        Synopsis = "Gets demo data."
                    }
                }
            };

            var writer = new MarkdownHelpWriter();
            writer.WriteModuleReadme(
                payload,
                moduleName: "DemoModule",
                readmePath: Path.Combine(docs, "Readme.md"),
                docsPath: docs);

            var text = File.ReadAllText(Path.Combine(docs, "Readme.md"));
            Assert.Contains("[about_Custom](About/about_Custom.md)", text);
            Assert.DoesNotContain("[README](About/README.md)", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void XmlDocCommentEnricher_SimplifiesCrefTokensInDescriptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-xmldoc-cref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assemblyPath = Path.Combine(root, "DemoModule.dll");
            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            File.WriteAllText(assemblyPath, string.Empty);

            File.WriteAllText(xmlPath, """
<doc>
  <members>
    <member name="T:Demo.Namespace.MyCommand">
      <summary>Output path used with <see cref="P:PSPublishModule.InvokeDotNetPublishCommand.JsonOnly"/>.</summary>
      <remarks>Accepts <see cref="T:PowerForge.ArtefactCopyMapping[]"/> entries.</remarks>
    </member>
    <member name="P:Demo.Namespace.MyCommand.ConfigPath">
      <summary>See <see cref="P:Demo.Namespace.MyCommand.ConfigPath"/> for configuration path.</summary>
    </member>
  </members>
</doc>
""");

            var payload = new DocumentationExtractionPayload
            {
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Demo",
                        CommandType = "Cmdlet",
                        ImplementingType = "Demo.Namespace.MyCommand",
                        AssemblyPath = assemblyPath,
                        Synopsis = string.Empty,
                        Description = string.Empty,
                        Parameters = new List<DocumentationParameterHelp>
                        {
                            new() { Name = "ConfigPath", Description = string.Empty }
                        }
                    }
                }
            };

            new XmlDocCommentEnricher(new NullLogger()).Enrich(payload);

            var cmd = Assert.Single(payload.Commands);
            Assert.Contains("JsonOnly", cmd.Synopsis);
            Assert.DoesNotContain("P:PSPublishModule.InvokeDotNetPublishCommand.JsonOnly", cmd.Synopsis);
            Assert.Contains("ArtefactCopyMapping[]", cmd.Description);
            Assert.DoesNotContain("T:PowerForge.ArtefactCopyMapping[]", cmd.Description);

            var parameter = Assert.Single(cmd.Parameters);
            Assert.Contains("ConfigPath", parameter.Description);
            Assert.DoesNotContain("P:Demo.Namespace.MyCommand.ConfigPath", parameter.Description);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void XmlDocCommentEnricher_PreservesExampleCodeIndentation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-xmldoc-example-indent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assemblyPath = Path.Combine(root, "DemoModule.dll");
            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            File.WriteAllText(assemblyPath, string.Empty);

            File.WriteAllText(xmlPath, """
<doc>
  <members>
    <member name="T:Demo.Namespace.MyCommand">
      <summary>Builds demo data.</summary>
      <example>
        <summary>Indented scriptblock example</summary>
        <code>
Invoke-Demo -Settings {
    New-ConfigurationModule -Type RequiredModule -Name 'Pester'
    New-ConfigurationBuild -Enable -InstallMissingModules
}
        </code>
      </example>
    </member>
  </members>
</doc>
""");

            var payload = new DocumentationExtractionPayload
            {
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Demo",
                        CommandType = "Cmdlet",
                        ImplementingType = "Demo.Namespace.MyCommand",
                        AssemblyPath = assemblyPath,
                        Synopsis = string.Empty,
                        Description = string.Empty,
                        Examples = new List<DocumentationExampleHelp>()
                    }
                }
            };

            new XmlDocCommentEnricher(new NullLogger()).Enrich(payload);

            var cmd = Assert.Single(payload.Commands);
            var example = Assert.Single(cmd.Examples);
            var normalizedCode = example.Code.Replace("\r\n", "\n");
            Assert.StartsWith("Invoke-Demo -Settings {", normalizedCode);
            Assert.Contains("\n    New-ConfigurationModule", normalizedCode);
            Assert.Contains("\n    New-ConfigurationBuild", normalizedCode);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void XmlDocCommentEnricher_EnrichesCommandNotes_AndTypeDescriptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-xmldoc-alerts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assemblyPath = Path.Combine(root, "DemoModule.dll");
            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            File.WriteAllText(assemblyPath, string.Empty);

            File.WriteAllText(xmlPath, """
<doc>
  <members>
    <member name="T:Demo.Namespace.MyCommand">
      <summary>Cmdlet summary.</summary>
      <list type="alertSet">
        <item>
          <term>Important</term>
          <description>
            <para>Run this command only after validation.</para>
            <para>It changes generated help output.</para>
          </description>
        </item>
      </list>
    </member>
    <member name="T:Demo.Namespace.MyInputType">
      <summary>Pipeline input object.</summary>
    </member>
    <member name="T:Demo.Namespace.MyOutputType">
      <remarks>Structured output object.</remarks>
    </member>
  </members>
</doc>
""");

            var payload = new DocumentationExtractionPayload
            {
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Demo",
                        CommandType = "Cmdlet",
                        ImplementingType = "Demo.Namespace.MyCommand",
                        AssemblyPath = assemblyPath,
                        Synopsis = string.Empty,
                        Description = string.Empty,
                        Inputs = new List<DocumentationTypeHelp>
                        {
                            new() { Name = "MyInputType", ClrTypeName = "Demo.Namespace.MyInputType", Description = string.Empty }
                        },
                        Outputs = new List<DocumentationTypeHelp>
                        {
                            new() { Name = "MyOutputType", ClrTypeName = "Demo.Namespace.MyOutputType", Description = string.Empty }
                        }
                    }
                }
            };

            new XmlDocCommentEnricher(new NullLogger()).Enrich(payload);

            var cmd = Assert.Single(payload.Commands);
            var note = Assert.Single(cmd.Notes);
            Assert.Equal("Important", note.Title);
            Assert.Contains("Run this command only after validation.", note.Text);
            Assert.Contains("It changes generated help output.", note.Text);

            var input = Assert.Single(cmd.Inputs);
            Assert.Equal("Pipeline input object.", input.Description);

            var output = Assert.Single(cmd.Outputs);
            Assert.Equal("Structured output object.", output.Description);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void XmlDocCommentEnricher_PreservesLegacyTopLevelParas_AndExamplePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-xmldoc-legacy-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assemblyPath = Path.Combine(root, "LegacyModule.dll");
            var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
            File.WriteAllText(assemblyPath, string.Empty);

            File.WriteAllText(xmlPath, """
<doc>
  <members>
    <member name="T:Demo.Namespace.LegacyCommand">
      <summary>Legacy synopsis.</summary>
      <para>First legacy paragraph.</para>
      <para>Second legacy paragraph.</para>
      <example>
        <summary>Render legacy output</summary>
        <prefix>PS&gt; </prefix>
        <code>
          Get-LegacyThing `
            -Name 'Alpha'
        </code>
        <para>Legacy example remarks.</para>
      </example>
    </member>
  </members>
</doc>
""");

            var payload = new DocumentationExtractionPayload
            {
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Get-LegacyThing",
                        CommandType = "Cmdlet",
                        ImplementingType = "Demo.Namespace.LegacyCommand",
                        AssemblyPath = assemblyPath,
                        Synopsis = string.Empty,
                        Description = string.Empty
                    }
                }
            };

            new XmlDocCommentEnricher(new NullLogger()).Enrich(payload);

            var cmd = Assert.Single(payload.Commands);
            Assert.Equal("Legacy synopsis.", cmd.Synopsis);
            Assert.Contains("First legacy paragraph.", cmd.Description);
            Assert.Contains("Second legacy paragraph.", cmd.Description);

            var example = Assert.Single(cmd.Examples);
            Assert.Equal("PS> ", example.Introduction);
            Assert.Contains("Get-LegacyThing `", example.Code);
            Assert.Contains("-Name 'Alpha'", example.Code);
            Assert.Equal("Legacy example remarks.", example.Remarks);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MamlHelpWriter_WritesCommandNotes_WhenPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-maml-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "TestModule",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Thing",
                        CommandType = "Cmdlet",
                        Synopsis = "Does a thing.",
                        Description = "Does a thing.",
                        Notes = new List<DocumentationNoteHelp>
                        {
                            new() { Title = "Caution", Text = "This modifies generated help files." }
                        }
                    }
                }
            };

            var writer = new MamlHelpWriter();
            var path = writer.WriteExternalHelpFile(payload, "TestModule", root);
            var doc = XDocument.Load(path);
            XNamespace mamlNs = "http://schemas.microsoft.com/maml/2004/10";

            Assert.Contains(doc.Descendants(mamlNs + "title"), element => element.Value == "Caution");
            Assert.Contains(doc.Descendants(mamlNs + "para"), element => element.Value == "This modifies generated help files.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MamlHelpWriter_WritesExampleIntroduction_WhenPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-maml-introduction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "TestModule",
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Invoke-Thing",
                        CommandType = "Cmdlet",
                        Synopsis = "Does a thing.",
                        Description = "Does a thing.",
                        Examples = new List<DocumentationExampleHelp>
                        {
                            new()
                            {
                                Title = "EXAMPLE 1",
                                Introduction = "PS> ",
                                Code = "Invoke-Thing -Name 'Alpha'",
                                Remarks = "Runs the command."
                            }
                        }
                    }
                }
            };

            var writer = new MamlHelpWriter();
            var path = writer.WriteExternalHelpFile(payload, "TestModule", root);
            var doc = XDocument.Load(path);
            XNamespace mamlNs = "http://schemas.microsoft.com/maml/2004/10";
            XNamespace devNs = "http://schemas.microsoft.com/maml/dev/2004/10";

            Assert.Contains(doc.Descendants(mamlNs + "introduction"), element => element.Value.Contains("PS> ", StringComparison.Ordinal));
            Assert.Contains(doc.Descendants(devNs + "code"), element => element.Value.Contains("Invoke-Thing -Name 'Alpha'", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GeneratedHelp_GetHelp_PreservesIntroductionAndNotes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-gethelp-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "IntegrationProbeModule";
            var psm1Path = Path.Combine(root, moduleName + ".psm1");
            var psd1Path = Path.Combine(root, moduleName + ".psd1");
            var culturePath = Path.Combine(root, "en-US");
            Directory.CreateDirectory(culturePath);

            File.WriteAllText(psm1Path, """
function Test-IntegrationProbe {
    [CmdletBinding()]
    param()

    'ok'
}

Export-ModuleMember -Function Test-IntegrationProbe
""");

            File.WriteAllText(psd1Path, """
@{
    RootModule = 'IntegrationProbeModule.psm1'
    ModuleVersion = '1.0.0'
    GUID = '22222222-2222-2222-2222-222222222222'
    FunctionsToExport = @('Test-IntegrationProbe')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""");

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = moduleName,
                Commands = new List<DocumentationCommandHelp>
                {
                    new()
                    {
                        Name = "Test-IntegrationProbe",
                        CommandType = "Function",
                        Synopsis = "Probe synopsis.",
                        Description = "First paragraph." + Environment.NewLine + Environment.NewLine + "Second paragraph.",
                        Notes = new List<DocumentationNoteHelp>
                        {
                            new() { Title = "Caution", Text = "First note para." + Environment.NewLine + Environment.NewLine + "Second note para." }
                        },
                        Examples = new List<DocumentationExampleHelp>
                        {
                            new()
                            {
                                Title = "----------  Example 1: Demo  ----------",
                                Introduction = "PS> ",
                                Code = "Test-IntegrationProbe",
                                Remarks = "Example remarks line 1." + Environment.NewLine + Environment.NewLine + "Example remarks line 2."
                            }
                        }
                    }
                }
            };

            new MamlHelpWriter().WriteExternalHelpFile(payload, moduleName, culturePath);

            var escapedManifestPath = psd1Path.Replace("'", "''", StringComparison.Ordinal);
            var script = $"""
$ErrorActionPreference = 'Stop'
Import-Module '{escapedManifestPath}' -Force
$full = Get-Help Test-IntegrationProbe -Full | Out-String -Width 200
$examples = Get-Help Test-IntegrationProbe -Examples | Out-String -Width 200
$exampleObject = (Get-Help Test-IntegrationProbe -Full).Examples.Example | ConvertTo-Json -Depth 8 -Compress
Write-Output 'FULL>>'
Write-Output $full
Write-Output 'EXAMPLES>>'
Write-Output $examples
Write-Output 'OBJECT>>'
Write-Output $exampleObject
""";

            var runner = new PowerShellRunner();
            var result = runner.Run(PowerShellRunRequest.ForCommand(
                commandText: script,
                timeout: TimeSpan.FromSeconds(30),
                preferPwsh: true,
                workingDirectory: root));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Caution", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("PS> Test-IntegrationProbe", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("\"introduction\":[{\"Text\":\"PS> \"}]", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static bool HasIsolatedLf(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i == 0 || text[i - 1] != '\r') return true;
        }
        return false;
    }
}
