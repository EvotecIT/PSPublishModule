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
class InvalidTextDefault {
    [string] ToString() {
        return [string][char]0xD800
    }
}

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

        $invalidTextAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $invalidTextDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $invalidTextDefault.Value = [InvalidTextDefault]::new()
        $invalidTextAttributes.Add($invalidTextDefault)
        $parameters.Add('InvalidText', [System.Management.Automation.RuntimeDefinedParameter]::new('InvalidText', [object], $invalidTextAttributes))

        $longHelpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $longHelpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $longHelpDefault.Help = 'x' * 80000
        $longHelpDefault.Value = 'ignored'
        $longHelpAttributes.Add($longHelpDefault)
        $parameters.Add('LongHelp', [System.Management.Automation.RuntimeDefinedParameter]::new('LongHelp', [string], $longHelpAttributes))

        $negativeDoubleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $negativeDoubleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $negativeDoubleDefault.Value = [System.BitConverter]::Int64BitsToDouble([long]::MinValue)
        $negativeDoubleAttributes.Add($negativeDoubleDefault)
        $parameters.Add('NegativeDouble', [System.Management.Automation.RuntimeDefinedParameter]::new('NegativeDouble', [double], $negativeDoubleAttributes))

        $negativeSingleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $negativeSingleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $negativeSingleDefault.Value = [System.BitConverter]::ToSingle([byte[]](0, 0, 0, 128), 0)
        $negativeSingleAttributes.Add($negativeSingleDefault)
        $parameters.Add('NegativeSingle', [System.Management.Automation.RuntimeDefinedParameter]::new('NegativeSingle', [single], $negativeSingleAttributes))

        $guidAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $guidDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $guidDefault.Value = [guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')
        $guidAttributes.Add($guidDefault)
        $parameters.Add('Guid', [System.Management.Automation.RuntimeDefinedParameter]::new('Guid', [guid], $guidAttributes))

        $versionAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $versionDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $versionDefault.Value = [version]::new(1, 2, 3, 4)
        $versionAttributes.Add($versionDefault)
        $parameters.Add('Version', [System.Management.Automation.RuntimeDefinedParameter]::new('Version', [version], $versionAttributes))

        $dateTimeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dateTimeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dateTimeDefault.Value = [datetime]::new(([long]639210116961234567), [System.DateTimeKind]::Local)
        $dateTimeAttributes.Add($dateTimeDefault)
        $parameters.Add('DateTime', [System.Management.Automation.RuntimeDefinedParameter]::new('DateTime', [datetime], $dateTimeAttributes))

        $dateTimeOffsetAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dateTimeOffsetDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dateTimeOffsetDefault.Value = [datetimeoffset]::ParseExact('2026-07-30T12:34:56.1234567+05:30', 'O', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
        $dateTimeOffsetAttributes.Add($dateTimeOffsetDefault)
        $parameters.Add('DateTimeOffset', [System.Management.Automation.RuntimeDefinedParameter]::new('DateTimeOffset', [datetimeoffset], $dateTimeOffsetAttributes))

        $timeSpanAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $timeSpanDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $timeSpanDefault.Value = [timespan]::ParseExact('1.02:03:04.5678900', 'c', [System.Globalization.CultureInfo]::InvariantCulture)
        $timeSpanAttributes.Add($timeSpanDefault)
        $parameters.Add('TimeSpan', [System.Management.Automation.RuntimeDefinedParameter]::new('TimeSpan', [timespan], $timeSpanAttributes))

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
                var invalidText = Assert.Single(command.Parameters, parameter => parameter.Name == "InvalidText");
                var longHelp = Assert.Single(command.Parameters, parameter => parameter.Name == "LongHelp");
                var negativeDouble = Assert.Single(command.Parameters, parameter => parameter.Name == "NegativeDouble");
                var negativeSingle = Assert.Single(command.Parameters, parameter => parameter.Name == "NegativeSingle");
                var guid = Assert.Single(command.Parameters, parameter => parameter.Name == "Guid");
                var version = Assert.Single(command.Parameters, parameter => parameter.Name == "Version");
                var dateTime = Assert.Single(command.Parameters, parameter => parameter.Name == "DateTime");
                var dateTimeOffset = Assert.Single(command.Parameters, parameter => parameter.Name == "DateTimeOffset");
                var timeSpan = Assert.Single(command.Parameters, parameter => parameter.Name == "TimeSpan");
                var accelerated = Assert.Single(
                    payload.Commands,
                    item => item.Name == "Get-AcceleratedOutput");
                var acceleratedOutput = Assert.Single(accelerated.Outputs);

                Assert.Equal(NestedExpression(80, "1"), nested.DefaultValue);
                Assert.Equal("authored display value", helpWins.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidSurrogate.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidSurrogateHelp.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidText.DefaultValue);
                Assert.Equal(80000, longHelp.DefaultValue.Length);
                Assert.All(longHelp.DefaultValue, character => Assert.Equal('x', character));
                Assert.Equal("-0.0", negativeDouble.DefaultValue);
                Assert.Equal("([single]-0.0)", negativeSingle.DefaultValue);
                Assert.Equal(
                    "[System.Guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')",
                    guid.DefaultValue);
                Assert.Equal("[System.Version]::Parse('1.2.3.4')", version.DefaultValue);
                Assert.Equal(
                    "[System.DateTime]::new(([long]639210116961234567), [System.DateTimeKind]::Local)",
                    dateTime.DefaultValue);
                Assert.Equal(
                    "[System.DateTimeOffset]::ParseExact('2026-07-30T12:34:56.1234567+05:30', 'O', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)",
                    dateTimeOffset.DefaultValue);
                Assert.Equal(
                    "[System.TimeSpan]::ParseExact('1.02:03:04.5678900', 'c', [System.Globalization.CultureInfo]::InvariantCulture)",
                    timeSpan.DefaultValue);
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
