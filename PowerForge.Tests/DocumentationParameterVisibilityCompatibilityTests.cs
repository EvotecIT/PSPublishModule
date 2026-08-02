using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationParameterVisibilityCompatibilityTests
{
    [Fact]
    public void Collector_UnwrapsNullableTypesAndOmitsDontShowParametersAcrossHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-parameter-visibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var modulePath = Path.Combine(root, "VisibilityFixture.psm1");
            var manifestPath = Path.Combine(root, "VisibilityFixture.psd1");
            File.WriteAllText(modulePath, """
enum VisibilityMode { Basic; Advanced }
class HiddenPayload { [string] $Value }
class VisiblePayload { [string] $Value }
function Get-VisibilityFixture {
    [CmdletBinding()]
    param(
        [Nullable[VisibilityMode]] $Mode,
        [Parameter(ValueFromPipeline = $true)] [Nullable[VisibilityMode][]] $Modes,
        [Parameter(Mandatory = $true, DontShow = $true, Position = 0, ValueFromPipelineByPropertyName = $true)] [string] $HiddenTransport
    )
}
function Get-GenericVisibilityCollisionFixture {
    [CmdletBinding()]
    param(
        [Parameter(DontShow = $true, ValueFromPipeline = $true)]
        [System.Collections.Generic.List[HiddenPayload]] $HiddenItems,
        [Parameter(ValueFromPipelineByPropertyName = $true)]
        [System.Collections.Generic.List[VisiblePayload]] $VisibleItems
    )
}
function Get-GenericVisibilityFixture {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [Nullable[System.Collections.Generic.KeyValuePair[string,int]]] $Pair
    )
}
function Get-MixedVisibilityFixture {
    [CmdletBinding(DefaultParameterSetName = 'Visible')]
    param(
        [Parameter(ParameterSetName = 'Hidden', DontShow = $true)]
        [Parameter(ParameterSetName = 'Visible')]
        [string] $Shared
    )
}
function Get-HiddenOnlySetFixture {
    [CmdletBinding(DefaultParameterSetName = 'Visible')]
    param(
        [string] $Shared,
        [Parameter(ParameterSetName = 'Visible')]
        [switch] $Visible,
        [Parameter(ParameterSetName = 'Hidden', DontShow = $true, Mandatory = $true)]
        [switch] $Secret
    )
}
function Get-HiddenOptionalDefaultSetFixture {
    [CmdletBinding(DefaultParameterSetName = 'HiddenDefault')]
    param(
        [Parameter(ParameterSetName = 'HiddenDefault', DontShow = $true)]
        [switch] $Secret,
        [Parameter(ParameterSetName = 'Visible')]
        [switch] $Visible
    )
}
function Get-SoleHiddenRequiredSetFixture {
    [CmdletBinding(DefaultParameterSetName = 'Only')]
    param(
        [Parameter(ParameterSetName = 'Only', DontShow = $true, Mandatory = $true)]
        [string] $Secret,
        [string] $Shared
    )
}
function Get-HiddenRequiredAllSetsFixture {
    [CmdletBinding(DefaultParameterSetName = 'ByName')]
    param(
        [Parameter(Mandatory = $true, DontShow = $true)]
        [string] $Secret,
        [Parameter(ParameterSetName = 'ByName')]
        [string] $Name,
        [Parameter(ParameterSetName = 'ById')]
        [int] $Id
    )
}
function GetDocumentationParameterDeclaringMetadata { throw 'Target helper shadow was invoked.' }
function TestDocumentationParameterDontShow { throw 'Target helper shadow was invoked.' }
function GetDocumentationRuntimeInputs { throw 'Target helper shadow was invoked.' }
Export-ModuleMember -Function Get-VisibilityFixture,Get-GenericVisibilityFixture,Get-GenericVisibilityCollisionFixture,Get-MixedVisibilityFixture,Get-HiddenOnlySetFixture,Get-HiddenOptionalDefaultSetFixture,Get-SoleHiddenRequiredSetFixture,Get-HiddenRequiredAllSetsFixture,GetDocumentationParameterDeclaringMetadata,TestDocumentationParameterDontShow,GetDocumentationRuntimeInputs
""", new UTF8Encoding(false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'VisibilityFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '34343434-3434-3434-3434-343434343434'
    Author = 'PowerForge.Tests'
    Description = 'Parameter visibility fixture.'
    FunctionsToExport = @('Get-VisibilityFixture','Get-GenericVisibilityFixture','Get-GenericVisibilityCollisionFixture','Get-MixedVisibilityFixture','Get-HiddenOnlySetFixture','Get-HiddenOptionalDefaultSetFixture','Get-SoleHiddenRequiredSetFixture','Get-HiddenRequiredAllSetsFixture','GetDocumentationParameterDeclaringMetadata','TestDocumentationParameterDontShow','GetDocumentationRuntimeInputs')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                DocumentationFallbackEnricher.Enrich(payload, new NullLogger());
                var command = Assert.Single(payload.Commands, item => item.Name == "Get-VisibilityFixture");
                Assert.Equal(2, command.Parameters.Count);
                var mode = Assert.Single(command.Parameters, parameter => parameter.Name == "Mode");
                var modes = Assert.Single(command.Parameters, parameter => parameter.Name == "Modes");

                Assert.Equal("Mode", mode.Name);
                Assert.Equal("VisibilityMode", mode.Type);
                Assert.Equal("VisibilityMode[]", modes.Type);
                Assert.Equal("VisibilityMode[]", Assert.Single(command.Inputs).Name);
                Assert.Equal(new[] { "Advanced", "Basic" }, mode.PossibleValues.OrderBy(value => value));
                Assert.All(command.Syntax, syntax => Assert.DoesNotContain("HiddenTransport", syntax.Text, StringComparison.Ordinal));
                Assert.All(command.Syntax, syntax => Assert.DoesNotContain("Nullable", syntax.Text, StringComparison.OrdinalIgnoreCase));

                var docsRoot = Path.Combine(root, host.Replace('.', '-') + "-docs");
                new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "VisibilityFixture", docsRoot);
                var markdown = File.ReadAllText(Path.Combine(docsRoot, "Get-VisibilityFixture.md"));
                Assert.DoesNotContain("HiddenTransport", markdown, StringComparison.Ordinal);
                Assert.DoesNotContain("Nullable", markdown, StringComparison.OrdinalIgnoreCase);

                var mamlRoot = Path.Combine(root, host.Replace('.', '-'));
                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, "VisibilityFixture", mamlRoot);
                var maml = File.ReadAllText(mamlPath);
                Assert.DoesNotContain("HiddenTransport", maml, StringComparison.Ordinal);
                Assert.DoesNotContain("Nullable", maml, StringComparison.OrdinalIgnoreCase);

                var genericCommand = Assert.Single(payload.Commands, item => item.Name == "Get-GenericVisibilityFixture");
                const string genericType = "System.Collections.Generic.KeyValuePair[System.String,System.Int32]";
                Assert.Equal(genericType, Assert.Single(genericCommand.Parameters).Type);
                Assert.Equal(genericType, Assert.Single(genericCommand.Inputs).Name);
                Assert.All(genericCommand.Syntax, syntax =>
                    Assert.DoesNotContain("KeyValuePair`2", syntax.Text, StringComparison.Ordinal));

                var collisionCommand = Assert.Single(payload.Commands, item => item.Name == "Get-GenericVisibilityCollisionFixture");
                Assert.Single(collisionCommand.Parameters, parameter => parameter.Name == "VisibleItems");
                var collisionInput = Assert.Single(collisionCommand.Inputs);
                Assert.Contains("VisiblePayload", collisionInput.ClrTypeName, StringComparison.Ordinal);
                Assert.DoesNotContain("HiddenPayload", collisionInput.ClrTypeName, StringComparison.Ordinal);

                var mixedCommand = Assert.Single(payload.Commands, item => item.Name == "Get-MixedVisibilityFixture");
                Assert.Single(mixedCommand.Parameters, parameter => parameter.Name == "Shared");
                Assert.Contains(mixedCommand.Syntax, syntax =>
                    syntax.Text.Contains("Shared", StringComparison.Ordinal));

                var hiddenOnlyCommand = Assert.Single(payload.Commands, item => item.Name == "Get-HiddenOnlySetFixture");
                Assert.Equal(2, hiddenOnlyCommand.Parameters.Count);
                Assert.Contains(hiddenOnlyCommand.Parameters, parameter => parameter.Name == "Shared");
                Assert.Contains(hiddenOnlyCommand.Parameters, parameter => parameter.Name == "Visible");
                Assert.DoesNotContain(hiddenOnlyCommand.Syntax, syntax =>
                    string.Equals(syntax.Name, "Hidden", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(hiddenOnlyCommand.Syntax, syntax =>
                    string.Equals(syntax.Name, "Visible", StringComparison.OrdinalIgnoreCase));

                var hiddenOnlyMarkdown = File.ReadAllText(Path.Combine(docsRoot, "Get-HiddenOnlySetFixture.md"));
                Assert.DoesNotContain("### Hidden", hiddenOnlyMarkdown, StringComparison.OrdinalIgnoreCase);
                var hiddenOnlyMaml = System.Xml.Linq.XDocument.Load(mamlPath);
                var hiddenOnlyCommandElement = Assert.Single(
                    hiddenOnlyMaml.Descendants(),
                    element => element.Name.LocalName == "command" &&
                               element.Descendants().Any(child =>
                                   child.Name.LocalName == "name" &&
                                   child.Value == "Get-HiddenOnlySetFixture"));
                Assert.DoesNotContain(
                    hiddenOnlyCommandElement.Descendants().SelectMany(element => element.Attributes("parameterSetName")),
                    attribute => string.Equals(attribute.Value, "Hidden", StringComparison.OrdinalIgnoreCase));

                var hiddenDefaultCommand = Assert.Single(payload.Commands, item => item.Name == "Get-HiddenOptionalDefaultSetFixture");
                Assert.Single(hiddenDefaultCommand.Parameters, parameter => parameter.Name == "Visible");
                var hiddenDefaultSyntax = Assert.Single(hiddenDefaultCommand.Syntax, syntax =>
                    string.Equals(syntax.Name, "HiddenDefault", StringComparison.OrdinalIgnoreCase));
                Assert.True(hiddenDefaultSyntax.IsDefault);
                Assert.DoesNotContain("Secret", hiddenDefaultSyntax.Text, StringComparison.Ordinal);
                Assert.Contains(hiddenDefaultCommand.Syntax, syntax =>
                    string.Equals(syntax.Name, "Visible", StringComparison.OrdinalIgnoreCase));

                var hiddenDefaultMarkdown = File.ReadAllText(Path.Combine(docsRoot, "Get-HiddenOptionalDefaultSetFixture.md"));
                Assert.Contains("### HiddenDefault (Default)", hiddenDefaultMarkdown, StringComparison.Ordinal);
                Assert.DoesNotContain("Secret", hiddenDefaultMarkdown, StringComparison.Ordinal);
                var hiddenDefaultCommandElement = Assert.Single(
                    hiddenOnlyMaml.Descendants(),
                    element => element.Name.LocalName == "command" &&
                               element.Descendants().Any(child =>
                                   child.Name.LocalName == "name" &&
                                   child.Value == "Get-HiddenOptionalDefaultSetFixture"));
                Assert.Contains(
                    hiddenDefaultCommandElement.Descendants().Where(element => element.Name.LocalName == "syntaxItem"),
                    element => string.Equals(
                        element.Attribute("parameterSetName")?.Value,
                        "HiddenDefault",
                        StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain("Secret", hiddenDefaultCommandElement.Value, StringComparison.Ordinal);

                var soleHiddenCommand = Assert.Single(payload.Commands, item => item.Name == "Get-SoleHiddenRequiredSetFixture");
                Assert.Single(soleHiddenCommand.Parameters, parameter => parameter.Name == "Shared");
                Assert.Empty(soleHiddenCommand.Syntax);
                Assert.Empty(soleHiddenCommand.Examples);

                var soleHiddenMarkdown = File.ReadAllText(Path.Combine(docsRoot, "Get-SoleHiddenRequiredSetFixture.md"));
                Assert.DoesNotContain("### Only", soleHiddenMarkdown, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "```powershell\nGet-SoleHiddenRequiredSetFixture\n```",
                    soleHiddenMarkdown.Replace("\r\n", "\n"),
                    StringComparison.Ordinal);
                var soleHiddenCommandElement = Assert.Single(
                    hiddenOnlyMaml.Descendants(),
                    element => element.Name.LocalName == "command" &&
                               element.Descendants().Any(child =>
                                   child.Name.LocalName == "name" &&
                                   child.Value == "Get-SoleHiddenRequiredSetFixture"));
                Assert.DoesNotContain(
                    soleHiddenCommandElement.Descendants(),
                    element => element.Name.LocalName == "syntaxItem");

                var hiddenAllSetsCommand = Assert.Single(payload.Commands, item => item.Name == "Get-HiddenRequiredAllSetsFixture");
                Assert.Equal(2, hiddenAllSetsCommand.Parameters.Count);
                Assert.Empty(hiddenAllSetsCommand.Syntax);
                Assert.Empty(hiddenAllSetsCommand.Examples);
                var hiddenAllSetsMarkdown = File.ReadAllText(Path.Combine(docsRoot, "Get-HiddenRequiredAllSetsFixture.md"));
                Assert.DoesNotContain(
                    "```powershell\nGet-HiddenRequiredAllSetsFixture\n```",
                    hiddenAllSetsMarkdown.Replace("\r\n", "\n"),
                    StringComparison.Ordinal);
                var hiddenAllSetsCommandElement = Assert.Single(
                    hiddenOnlyMaml.Descendants(),
                    element => element.Name.LocalName == "command" &&
                               element.Descendants().Any(child =>
                                   child.Name.LocalName == "name" &&
                                   child.Value == "Get-HiddenRequiredAllSetsFixture"));
                Assert.DoesNotContain(
                    hiddenAllSetsCommandElement.Descendants(),
                    element => element.Name.LocalName == "syntaxItem");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private sealed class ExecutablePowerShellRunner : IPowerShellRunner
    {
        private readonly string _executable;
        private readonly string _workingDirectory;
        private readonly PowerShellRunner _inner = new();

        public ExecutablePowerShellRunner(string executable, string workingDirectory)
        {
            _executable = executable;
            _workingDirectory = workingDirectory;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _inner.Run(new PowerShellRunRequest(
                request.ScriptPath!, request.Arguments, request.Timeout, request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory, request.EnvironmentVariables,
                _executable, request.CaptureOutput, request.CaptureError,
                request.OutputLineReceived, request.ErrorLineReceived));
    }
}
