using System.Runtime.Serialization.Json;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void Collector_PreservesKeysParametersWithoutSerializingMetadataObjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifestPath = Path.Combine(root, "KeysFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'KeysFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '56565656-5656-5656-5656-565656565656'
    FunctionsToExport = @('Get-KeysOnly', 'Get-KeysAndOther', 'Get-KeysDynamic')
    CmdletsToExport = @()
    AliasesToExport = @()
}
""", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "KeysFixture.psm1"), """
function Get-KeysOnly {
    [CmdletBinding()] param([string] $Keys)
}
function Get-KeysAndOther {
    [CmdletBinding()] param([string] $Keys, [int] $Other)
}
function Get-KeysDynamic {
<#
.SYNOPSIS
Shows keys.
.PARAMETER Keys
Keys to show.
#>
    [CmdletBinding()] param([string] $Keys)
    dynamicparam { }
    process { }
}
Export-ModuleMember -Function Get-KeysOnly, Get-KeysAndOther, Get-KeysDynamic
""", new UTF8Encoding(false));
            var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                Assert.Equal(3, payload.Commands.Count);
                var only = Assert.Single(payload.Commands, command => command.Name == "Get-KeysOnly");
                var other = Assert.Single(payload.Commands, command => command.Name == "Get-KeysAndOther");
                var dynamic = Assert.Single(payload.Commands, command => command.Name == "Get-KeysDynamic");
                Assert.Equal("Keys", Assert.Single(only.Parameters).Name);
                Assert.Equal(["Keys", "Other"], other.Parameters.Select(parameter => parameter.Name).ToArray());
                Assert.Equal("Keys", Assert.Single(dynamic.Parameters).Name);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

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
    moduleDescription = $surrogate
    commands = @([ordered]@{
        name = 'Get-TransportFixture'
        commandType = 'Function'
        synopsis = $surrogate
        description = $surrogate
        syntax = @([ordered]@{ name = $surrogate; isDefault = $true; text = $surrogate })
        parameters = @([ordered]@{
            name = 'Mode'
            type = 'String'
            description = $surrogate
            parameterSets = @($surrogate)
            parameterSetRequired = @{ $surrogate = $true }
            aliases = @($surrogate)
            possibleValues = @($surrogate)
            enumPossibleValues = @()
            hasValidateSet = $true
        })
        examples = @([ordered]@{ title = $surrogate; introduction = $surrogate; code = $surrogate; remarks = $surrogate })
        inputs = @([ordered]@{ name = 'System.String'; description = $surrogate })
        outputs = @()
        authoredOutputs = @([ordered]@{
            name = 'System.String'
            clrTypeName = 'System.String'
            canonicalTypeName = 'System.String'
            description = $surrogate
        })
        runtimeOutputs = @()
        relatedLinks = @([ordered]@{ text = $surrogate; uri = $surrogate })
        notes = @([ordered]@{ title = $surrogate; text = $surrogate })
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
                Assert.Equal('\uD800', Assert.Single(parameter.Aliases)[0]);
                Assert.Equal('\uD800', Assert.Single(parameter.PossibleValues)[0]);
                Assert.Equal('\uD800', authoredOutput.Description[0]);

                DocumentationMetadataNormalizer.Normalize(payload);
                Assert.Equal("([char]55296)", Assert.Single(parameter.Aliases));
                Assert.Equal("([char]55296)", Assert.Single(parameter.PossibleValues));
                Assert.Equal("([char]55296)", Assert.Single(command.Outputs).Description);
                Assert.Equal("([char]55296)", payload.ModuleDescription);
                Assert.Equal("([char]55296)", command.Synopsis);
                Assert.Equal("([char]55296)", command.Description);
                Assert.Equal("([char]55296)", parameter.Description);
                Assert.Equal("([char]55296)", Assert.Single(command.Examples).Code);
                Assert.Equal("([char]55296)", Assert.Single(command.Inputs).Description);
                Assert.Equal("([char]55296)", Assert.Single(command.RelatedLinks).Text);
                Assert.Equal("([char]55296)", Assert.Single(command.Notes).Text);

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
