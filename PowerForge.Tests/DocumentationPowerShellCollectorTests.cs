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
    FunctionsToExport = @('Get-CollectorFixture', 'Get-AcceleratedOutput')
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
        foreach ($index in 1..80) { $nested = ,$nested }
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

        $surrogateAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $surrogateDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $surrogateDefault.Value = [string][char]0xD800
        $surrogateAttributes.Add($surrogateDefault)
        $parameters.Add(
            'InvalidSurrogate',
            [System.Management.Automation.RuntimeDefinedParameter]::new(
                'InvalidSurrogate',
                [string],
                $surrogateAttributes))

        $surrogateHelpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $surrogateHelpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $surrogateHelpDefault.Help = [string][char]0xD800
        $surrogateHelpDefault.Value = 'ignored'
        $surrogateHelpAttributes.Add($surrogateHelpDefault)
        $parameters.Add(
            'InvalidSurrogateHelp',
            [System.Management.Automation.RuntimeDefinedParameter]::new(
                'InvalidSurrogateHelp',
                [string],
                $surrogateHelpAttributes))

        $parameters
    }
}

function Get-AcceleratedOutput {
    <#
    .EXTERNALHELP CollectorFixture-help.xml
    #>
    [OutputType([string])]
    [CmdletBinding()]
    param()

    'value'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var helpDirectory = Path.Combine(root, "en-US");
            Directory.CreateDirectory(helpDirectory);
            File.WriteAllText(Path.Combine(helpDirectory, "CollectorFixture-help.xml"), """
<?xml version="1.0" encoding="utf-8"?>
<helpItems schema="maml" xmlns="http://msh">
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-AcceleratedOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>AcceleratedOutput</command:noun>
      <maml:description>
        <maml:para>Returns a string value.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Returns a string value.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-AcceleratedOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>system.string</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>An authored accelerator description.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
</helpItems>
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(
                    payload.Commands,
                    item => item.Name == "Get-CollectorFixture");
                var nested = Assert.Single(command.Parameters, parameter => parameter.Name == "Nested");
                var helpWins = Assert.Single(command.Parameters, parameter => parameter.Name == "HelpWins");
                var invalidSurrogate = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "InvalidSurrogate");
                var invalidSurrogateHelp = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "InvalidSurrogateHelp");
                var accelerated = Assert.Single(
                    payload.Commands,
                    item => item.Name == "Get-AcceleratedOutput");
                var acceleratedOutput = Assert.Single(accelerated.Outputs);

                Assert.Equal(NestedExpression(80, "1"), nested.DefaultValue);
                Assert.Equal("authored display value", helpWins.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidSurrogate.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidSurrogateHelp.DefaultValue);
                Assert.Equal("System.String", acceleratedOutput.ClrTypeName);
                Assert.Equal("An authored accelerator description.", acceleratedOutput.Description);
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
