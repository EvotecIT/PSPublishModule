using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationOutputWhitespaceCompatibilityTests
{
    [Fact]
    public void DocumentationEngine_PreservesBoundaryWhitespaceInExtendedOutputTypesAcrossBothHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-output-whitespace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string moduleName = "OutputWhitespaceFixture";
            var modulePath = Path.Combine(root, moduleName + ".psm1");
            var manifestPath = Path.Combine(root, moduleName + ".psd1");
            File.WriteAllText(modulePath, """
function Get-WhitespaceOutput {
    [CmdletBinding()]
    [OutputType(' A ', 'A')]
    param()
}
""", new UTF8Encoding(false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'OutputWhitespaceFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '40404040-4040-4040-4040-404040404040'
    FunctionsToExport = @('Get-WhitespaceOutput')
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
                var outputs = Assert.Single(payload.Commands).Outputs;
                Assert.Contains(outputs, output => output.Name == " A " && output.ClrTypeName == " A ");
                Assert.Contains(outputs, output => output.Name == "A" && output.ClrTypeName == "A");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
                request.ScriptPath!,
                request.Arguments,
                request.Timeout,
                request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory,
                request.EnvironmentVariables,
                _executable,
                request.CaptureOutput,
                request.CaptureError,
                request.OutputLineReceived,
                request.ErrorLineReceived));
    }
}
