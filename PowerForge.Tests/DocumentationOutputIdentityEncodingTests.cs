using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationOutputIdentityEncodingTests
{
    [Fact]
    public void DocumentationEngine_KeepsXmlSafeOutputIdentityRenderingInjective()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-output-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "OutputIdentityFixture.psd1");
            File.WriteAllText(Path.Combine(root, "OutputIdentityFixture.psm1"), """
$functionText = "function Get-OutputIdentityFixture { [OutputType('A" + [char]1 + "', 'A([char]1)', 'A%u0001', 'A" + [char]10 + "B', 'A B', 'A%u000AB')] param() }"
. ([scriptblock]::Create($functionText))
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'OutputIdentityFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '77777777-7777-7777-7777-777777777777'
    FunctionsToExport = @('Get-OutputIdentityFixture')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);
            var outputNames = command.Outputs.Select(output => output.Name).ToList();
            Assert.Contains("A%u0001", outputNames);
            Assert.Contains("A([char]1)", outputNames);
            Assert.Contains("A%25u0001", outputNames);
            Assert.Equal(outputNames.Count, outputNames.Distinct(StringComparer.Ordinal).Count());

            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                payload, "OutputIdentityFixture", Path.Combine(root, "generated"));
            var markdownDirectory = Path.Combine(root, "markdown");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "OutputIdentityFixture", markdownDirectory);
            var maml = File.ReadAllText(mamlPath);
            var markdown = File.ReadAllText(Path.Combine(markdownDirectory, "Get-OutputIdentityFixture.md"));
            Assert.DoesNotContain('\u0001', maml);
            Assert.Contains("A%u0001", maml, StringComparison.Ordinal);
            Assert.Contains("A([char]1)", maml, StringComparison.Ordinal);
            Assert.Contains("A%25u0001", maml, StringComparison.Ordinal);
            var renderedNames = outputNames
                .Select(MarkdownDocumentBuilder.InlineIdentityCode)
                .ToList();
            Assert.Equal(renderedNames.Count, renderedNames.Distinct(StringComparer.Ordinal).Count());
            foreach (var renderedName in renderedNames)
            {
                Assert.Contains(renderedName, markdown, StringComparison.Ordinal);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }
}
