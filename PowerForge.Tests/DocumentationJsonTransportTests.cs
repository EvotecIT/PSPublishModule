using System.Runtime.Serialization.Json;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void DocumentationTransport_PreservesUnpairedSurrogatesAcrossBothPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-json-transport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "WriteTransportPayload.ps1");
            var outputPath = Path.Combine(root, "payload.json");
            var helperScript = EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.RuntimeValueHelpers.ps1");
            File.WriteAllText(scriptPath, "param([string]$OutputPath)" + Environment.NewLine +
                helperScript + Environment.NewLine + """
$surrogate = [string][char]0xD800
$result = [ordered]@{
    moduleName = 'TransportFixture'
    moduleVersion = '1.0.0'
    commands = @([ordered]@{
        name = 'Get-TransportFixture'
        commandType = 'Function'
        parameters = @([ordered]@{
            name = 'Mode'
            type = 'String'
            possibleValues = @($surrogate)
            enumPossibleValues = @()
            hasValidateSet = $true
        })
        outputs = @()
        authoredOutputs = @([ordered]@{
            name = 'System.String'
            clrTypeName = 'System.String'
            canonicalTypeName = 'System.String'
            description = $surrogate
        })
        runtimeOutputs = @()
    })
}
$json = ConvertToUtf8SafeJsonText ($result | ConvertTo-Json -Depth 20)
[System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var run = new ExecutablePowerShellRunner(host, root).Run(new PowerShellRunRequest(
                    scriptPath,
                    new[] { outputPath },
                    TimeSpan.FromMinutes(1)));
                Assert.True(run.ExitCode == 0, run.StdErr);

                var json = File.ReadAllText(outputPath, Encoding.UTF8);
                Assert.Contains("\\uD800", json, StringComparison.Ordinal);
                Assert.DoesNotContain('\uFFFD', json);

                using var stream = File.OpenRead(outputPath);
                var serializer = new DataContractJsonSerializer(
                    typeof(DocumentationExtractionPayload),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                var payload = Assert.IsType<DocumentationExtractionPayload>(serializer.ReadObject(stream));
                var command = Assert.Single(payload.Commands);
                var parameter = Assert.Single(command.Parameters);
                var authoredOutput = Assert.Single(command.AuthoredOutputs);
                Assert.Equal('\uD800', Assert.Single(parameter.PossibleValues)[0]);
                Assert.Equal('\uD800', authoredOutput.Description[0]);

                DocumentationMetadataNormalizer.Normalize(payload);
                Assert.Equal("([char]55296)", Assert.Single(parameter.PossibleValues));
                Assert.Equal("([char]55296)", Assert.Single(command.Outputs).Description);

                var hostOutput = Path.Combine(root, host.Replace('.', '-'));
                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, "TransportFixture", hostOutput);
                var docsPath = Path.Combine(hostOutput, "Docs");
                new MarkdownHelpWriter().WriteCommandHelpFiles(
                    payload,
                    "TransportFixture",
                    docsPath);
                var markdownPath = Path.Combine(docsPath, "Get-TransportFixture.md");
                foreach (var artifact in new[] { mamlPath, markdownPath })
                {
                    var text = File.ReadAllText(artifact);
                    Assert.Contains("([char]55296)", text, StringComparison.Ordinal);
                    Assert.DoesNotContain('\uD800', text);
                    Assert.DoesNotContain('\uFFFD', text);
                }
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
