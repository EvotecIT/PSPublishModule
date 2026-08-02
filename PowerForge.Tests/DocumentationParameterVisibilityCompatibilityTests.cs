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
function Get-VisibilityFixture {
    [CmdletBinding()]
    param(
        [Nullable[VisibilityMode]] $Mode,
        [Parameter(ValueFromPipeline = $true)] [Nullable[VisibilityMode][]] $Modes,
        [Parameter(Mandatory = $true, DontShow = $true, Position = 0)] [string] $HiddenTransport
    )
}
function GetDocumentationParameterDeclaringMetadata { throw 'Target helper shadow was invoked.' }
function TestDocumentationParameterDontShow { throw 'Target helper shadow was invoked.' }
Export-ModuleMember -Function Get-VisibilityFixture,GetDocumentationParameterDeclaringMetadata,TestDocumentationParameterDontShow
""", new UTF8Encoding(false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'VisibilityFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '34343434-3434-3434-3434-343434343434'
    Author = 'PowerForge.Tests'
    Description = 'Parameter visibility fixture.'
    FunctionsToExport = @('Get-VisibilityFixture','GetDocumentationParameterDeclaringMetadata','TestDocumentationParameterDontShow')
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
