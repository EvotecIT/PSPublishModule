using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebApiDocsGeneratorXrefTests
{
    [Fact]
    public void Generate_CSharpApiDocs_WritesXrefMapWithExpectedAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-csharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Assembly</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Widget">
                      <summary>Widget docs.</summary>
                    </member>
                    <member name="M:MyNamespace.Widget.Run(System.String,System.Int32)">
                      <summary>Runs widget pipeline.</summary>
                      <param name="name">Widget name.</param>
                      <param name="retries">Retry count.</param>
                    </member>
                    <member name="P:MyNamespace.Widget.Name">
                      <summary>Widget name.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "both"
            });

            Assert.False(string.IsNullOrWhiteSpace(result.XrefPath));
            Assert.True(File.Exists(result.XrefPath!));

            using var doc = JsonDocument.Parse(File.ReadAllText(result.XrefPath!));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            var widget = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "MyNamespace.Widget", StringComparison.Ordinal));
            var run = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "M:MyNamespace.Widget.Run(System.String,System.Int32)", StringComparison.Ordinal));
            var nameProperty = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "P:MyNamespace.Widget.Name", StringComparison.Ordinal));

            Assert.Equal("/api/mynamespace-widget/", widget.GetProperty("href").GetString());
            var aliases = widget.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString() ?? string.Empty).ToArray();
            Assert.Contains("T:MyNamespace.Widget", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("global::MyNamespace.Widget", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Widget", aliases, StringComparer.OrdinalIgnoreCase);

            Assert.Equal("/api/mynamespace-widget/#method-run-string-int32", run.GetProperty("href").GetString());
            var runAliases = run.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString() ?? string.Empty).ToArray();
            Assert.Contains("MyNamespace.Widget.Run(System.String,System.Int32)", runAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("global::MyNamespace.Widget.Run", runAliases, StringComparer.OrdinalIgnoreCase);

            Assert.Equal("/api/mynamespace-widget/#property-name", nameProperty.GetProperty("href").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellApiDocs_WritesXrefMapWithModuleAndAboutAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-powershell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <command:commandType>Cmdlet</command:commandType>
                      <maml:description><maml:para>Gets sample items.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Get-SampleCmdlet</command:name>
                        <command:parameter required="false" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue>string</command:parameterValue>
                          <maml:description><maml:para>Name filter.</maml:para></maml:description>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                    <command:parameters>
                      <command:parameter required="false" position="named">
                        <maml:name>Name</maml:name>
                        <command:parameterValue>string</command:parameterValue>
                        <maml:description><maml:para>Name filter.</maml:para></maml:description>
                      </command:parameter>
                    </command:parameters>
                  </command:command>
                </helpItems>
                """);
            File.WriteAllText(Path.Combine(root, "about_SampleTopic.help.txt"),
                """
                # about_SampleTopic

                About topic docs.
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                BaseUrl = "/api/powershell",
                Format = "html"
            });

            Assert.False(string.IsNullOrWhiteSpace(result.XrefPath));
            Assert.True(File.Exists(result.XrefPath!));

            using var doc = JsonDocument.Parse(File.ReadAllText(result.XrefPath!));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            var command = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "Get-SampleCmdlet", StringComparison.OrdinalIgnoreCase));
            var about = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "about_SampleTopic", StringComparison.OrdinalIgnoreCase));
            var parameter = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "parameter:Get-SampleCmdlet.Name", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("/api/powershell/get-samplecmdlet/", command.GetProperty("href").GetString());
            var commandAliases = command.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString() ?? string.Empty).ToArray();
            Assert.Contains("ps:Get-SampleCmdlet", commandAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("command:Get-SampleCmdlet", commandAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Sample.Module\\Get-SampleCmdlet", commandAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Sample.Module::Get-SampleCmdlet", commandAliases, StringComparer.OrdinalIgnoreCase);

            var aboutAliases = about.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString() ?? string.Empty).ToArray();
            Assert.Contains("about:about_SampleTopic", aboutAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("about:SampleTopic", aboutAliases, StringComparer.OrdinalIgnoreCase);

            Assert.StartsWith("/api/powershell/get-samplecmdlet/#method-get-samplecmdlet", parameter.GetProperty("href").GetString(), StringComparison.OrdinalIgnoreCase);
            var parameterAliases = parameter.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString() ?? string.Empty).ToArray();
            Assert.Contains("Get-SampleCmdlet.Name", parameterAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Sample.Module\\Get-SampleCmdlet.Name", parameterAliases, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_ApiDocs_DoesNotWriteXrefMapWhenDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Assembly</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Widget">
                      <summary>Widget docs.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "json",
                GenerateXrefMap = false
            });

            Assert.Null(result.XrefPath);
            Assert.False(File.Exists(Path.Combine(outputPath, "xrefmap.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CSharpApiDocs_DoesNotIncludeMemberEntries_WhenGenerateMemberXrefsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-no-members-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Assembly</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Widget">
                      <summary>Widget docs.</summary>
                    </member>
                    <member name="M:MyNamespace.Widget.Run(System.String)">
                      <summary>Runs widget pipeline.</summary>
                      <param name="name">Widget name.</param>
                    </member>
                    <member name="P:MyNamespace.Widget.Name">
                      <summary>Widget name.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "both",
                GenerateMemberXrefs = false
            });

            Assert.False(string.IsNullOrWhiteSpace(result.XrefPath));
            Assert.True(File.Exists(result.XrefPath!));

            using var doc = JsonDocument.Parse(File.ReadAllText(result.XrefPath!));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            Assert.Contains(references, reference => string.Equals(reference.GetProperty("uid").GetString(), "MyNamespace.Widget", StringComparison.Ordinal));
            Assert.DoesNotContain(references, reference => string.Equals(reference.GetProperty("uid").GetString(), "M:MyNamespace.Widget.Run(System.String)", StringComparison.Ordinal));
            Assert.DoesNotContain(references, reference => string.Equals(reference.GetProperty("uid").GetString(), "P:MyNamespace.Widget.Name", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CSharpApiDocs_AppliesMemberXrefKindsAndMaxPerType()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-kinds-max-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Assembly</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Widget">
                      <summary>Widget docs.</summary>
                    </member>
                    <member name="M:MyNamespace.Widget.Run(System.String)">
                      <summary>Runs widget pipeline.</summary>
                      <param name="name">Widget name.</param>
                    </member>
                    <member name="P:MyNamespace.Widget.Name">
                      <summary>Widget name.</summary>
                    </member>
                    <member name="F:MyNamespace.Widget.Count">
                      <summary>Widget count.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "both",
                MemberXrefMaxPerType = 1
            };
            options.MemberXrefKinds.AddRange(new[] { "methods", "properties" });

            var result = WebApiDocsGenerator.Generate(options);

            Assert.False(string.IsNullOrWhiteSpace(result.XrefPath));
            Assert.True(File.Exists(result.XrefPath!));

            using var doc = JsonDocument.Parse(File.ReadAllText(result.XrefPath!));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            var widgetMemberRefs = references
                .Where(reference =>
                {
                    var uid = reference.GetProperty("uid").GetString() ?? string.Empty;
                    return uid.StartsWith("M:MyNamespace.Widget.", StringComparison.Ordinal) ||
                           uid.StartsWith("P:MyNamespace.Widget.", StringComparison.Ordinal) ||
                           uid.StartsWith("F:MyNamespace.Widget.", StringComparison.Ordinal) ||
                           uid.StartsWith("E:MyNamespace.Widget.", StringComparison.Ordinal);
                })
                .ToArray();

            Assert.Single(widgetMemberRefs);
            Assert.Equal("M:MyNamespace.Widget.Run(System.String)", widgetMemberRefs[0].GetProperty("uid").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellApiDocs_MemberXrefKindsCanDisableParameterEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-apidocs-xref-powershell-no-params-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <command:commandType>Cmdlet</command:commandType>
                      <maml:description><maml:para>Gets sample items.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem>
                        <command:name>Get-SampleCmdlet</command:name>
                        <command:parameter required="false" position="named">
                          <maml:name>Name</maml:name>
                          <command:parameterValue>string</command:parameterValue>
                        </command:parameter>
                      </command:syntaxItem>
                    </command:syntax>
                  </command:command>
                </helpItems>
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                BaseUrl = "/api/powershell",
                Format = "html"
            };
            options.MemberXrefKinds.Add("methods");

            var result = WebApiDocsGenerator.Generate(options);
            Assert.False(string.IsNullOrWhiteSpace(result.XrefPath));
            Assert.True(File.Exists(result.XrefPath!));

            using var doc = JsonDocument.Parse(File.ReadAllText(result.XrefPath!));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            Assert.DoesNotContain(references, reference =>
            {
                var uid = reference.GetProperty("uid").GetString();
                return uid is not null &&
                       uid.StartsWith("parameter:Get-SampleCmdlet.", StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
