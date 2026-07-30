using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void DocumentationEngine_TransfersNestedDefaultsWithoutSerializingIgnoredValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "CollectorFixture.psd1");
            var modulePath = Path.Combine(root, "CollectorFixture.psm1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'CollectorFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '77777777-7777-7777-7777-777777777777'
    FunctionsToExport = @('Get-CollectorFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(modulePath, """
function Get-CollectorFixture {
    [CmdletBinding()]
    param()

    dynamicparam {
        $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()

        $nestedDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $nested = 1
        foreach ($index in 1..12) { $nested = ,$nested }
        $nestedDefault.Value = $nested
        $attributes.Add($nestedDefault)

        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        $parameters.Add(
            'Nested',
            [System.Management.Automation.RuntimeDefinedParameter]::new(
                'Nested',
                [object],
                $attributes))

        $helpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $helpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $helpDefault.Help = 'authored display value'
        $ignored = 1
        foreach ($index in 1..80) { $ignored = ,$ignored }
        $helpDefault.Value = $ignored
        $helpAttributes.Add($helpDefault)
        $parameters.Add(
            'HelpWins',
            [System.Management.Automation.RuntimeDefinedParameter]::new(
                'HelpWins',
                [object],
                $helpAttributes))

        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(payload.Commands);
                var nested = Assert.Single(command.Parameters, parameter => parameter.Name == "Nested");
                var helpWins = Assert.Single(command.Parameters, parameter => parameter.Name == "HelpWins");

                Assert.Equal(NestedExpression(12, "1"), nested.DefaultValue);
                Assert.Equal("authored display value", helpWins.DefaultValue);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    private static string NestedExpression(int depth, string value)
    {
        var result = value;
        for (var index = 0; index < depth; index++)
            result = "@(" + result + ")";
        return result;
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
