using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationDefaultLiteralCompatibilityTests
{
    [Fact]
    public void DocumentationEngine_PreservesTypedPowerShellOnlyDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-default-literals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "DefaultLiteralFixture.psd1");
            File.WriteAllText(Path.Combine(root, "DefaultLiteralFixture.psm1"), """
function Get-DefaultLiteralFixture {
    [CmdletBinding()]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()

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

        $decimalAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $decimalDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $decimalDefault.Value = [decimal]::Parse('0.1234567890123456789012345678', [System.Globalization.CultureInfo]::InvariantCulture)
        $decimalAttributes.Add($decimalDefault)
        $parameters.Add('PreciseDecimal', [System.Management.Automation.RuntimeDefinedParameter]::new('PreciseDecimal', [decimal], $decimalAttributes))

        $bigIntegerAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $bigIntegerDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $bigIntegerDefault.Value = [System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)
        $bigIntegerAttributes.Add($bigIntegerDefault)
        $parameters.Add('BigInteger', [System.Management.Automation.RuntimeDefinedParameter]::new('BigInteger', [System.Numerics.BigInteger], $bigIntegerAttributes))

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

        $uriAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $uriDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $uriDefault.Value = [uri]::new("https://example.com/a'b?x=1")
        $uriAttributes.Add($uriDefault)
        $parameters.Add('Uri', [System.Management.Automation.RuntimeDefinedParameter]::new('Uri', [uri], $uriAttributes))

        $dictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dictionaryDefault.Value = [ordered]@{
            alpha = 1
            endpoint = [uri]::new('relative/path', [System.UriKind]::Relative)
        }
        $dictionaryAttributes.Add($dictionaryDefault)
        $parameters.Add('Dictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('Dictionary', [System.Collections.IDictionary], $dictionaryAttributes))

        $cyclicAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $cyclicDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $cyclicValue = [System.Collections.ArrayList]::new()
        [void]$cyclicValue.Add($cyclicValue)
        $cyclicDefault.Value = $cyclicValue
        $cyclicAttributes.Add($cyclicDefault)
        $parameters.Add('CyclicCollection', [System.Management.Automation.RuntimeDefinedParameter]::new('CyclicCollection', [object], $cyclicAttributes))

        $matrixAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $matrixDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $matrix = [int[,]]::new(2, 2)
        $matrix[0, 0] = 1
        $matrix[0, 1] = 2
        $matrix[1, 0] = 3
        $matrix[1, 1] = 4
        $matrixDefault.Value = $matrix
        $matrixAttributes.Add($matrixDefault)
        $parameters.Add('Matrix', [System.Management.Automation.RuntimeDefinedParameter]::new('Matrix', [int[,]], $matrixAttributes))

        $dateOnlyAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dateOnlyDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dateOnlyDefault.Value = [System.DateOnly]::FromDayNumber(739827)
        $dateOnlyAttributes.Add($dateOnlyDefault)
        $parameters.Add('DateOnly', [System.Management.Automation.RuntimeDefinedParameter]::new('DateOnly', [System.DateOnly], $dateOnlyAttributes))

        $timeOnlyAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $timeOnlyDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $timeOnlyDefault.Value = [System.TimeOnly]::new(([long]452961234567))
        $timeOnlyAttributes.Add($timeOnlyDefault)
        $parameters.Add('TimeOnly', [System.Management.Automation.RuntimeDefinedParameter]::new('TimeOnly', [System.TimeOnly], $timeOnlyAttributes))

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

        $scriptAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $scriptDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $scriptDefault.Value = [scriptblock]::Create("1+2`n" + '```')
        $scriptAttributes.Add($scriptDefault)
        $parameters.Add('Script', [System.Management.Automation.RuntimeDefinedParameter]::new('Script', [scriptblock], $scriptAttributes))

        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'DefaultLiteralFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '99999999-9999-9999-9999-999999999999'
    FunctionsToExport = @('Get-DefaultLiteralFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var helpDirectory = Path.Combine(root, "en-US");
            Directory.CreateDirectory(helpDirectory);
            File.WriteAllText(Path.Combine(helpDirectory, "DefaultLiteralFixture-help.xml"), """
<?xml version="1.0" encoding="utf-8"?>
<helpItems schema="maml" xmlns="http://msh">
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-DefaultLiteralFixture</command:name>
      <command:verb>Get</command:verb>
      <command:noun>DefaultLiteralFixture</command:noun>
      <maml:description><maml:para>Exercises typed defaults.</maml:para></maml:description>
    </command:details>
    <maml:description><maml:para>Exercises typed defaults.</maml:para></maml:description>
    <command:syntax><command:syntaxItem><maml:name>Get-DefaultLiteralFixture</maml:name></command:syntaxItem></command:syntax>
    <command:parameters>
      <command:parameter required="false" variableLength="false" globbing="false" pipelineInput="False" position="named" aliases="None">
        <maml:name>CyclicCollection</maml:name>
        <command:parameterValue required="false" variableLength="false">Object</command:parameterValue>
        <dev:type><maml:name>Object</maml:name></dev:type>
        <dev:defaultValue>Stale external-help default</dev:defaultValue>
      </command:parameter>
    </command:parameters>
    <command:inputTypes />
    <command:returnValues />
  </command:command>
</helpItems>
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);

            Assert.Equal("-0.0", Default("NegativeDouble"));
            Assert.Equal("([single]-0.0)", Default("NegativeSingle"));
            Assert.Equal(
                "[System.Decimal]::Parse('0.1234567890123456789012345678', [System.Globalization.CultureInfo]::InvariantCulture)",
                Default("PreciseDecimal"));
            Assert.Equal(
                "[System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)",
                Default("BigInteger"));
            Assert.Equal(
                "[System.Guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')",
                Default("Guid"));
            Assert.Equal(
                "[System.Version]::Parse('1.2.3.4')",
                Default("Version"));
            Assert.Equal(
                "[System.Uri]::new('https://example.com/a''b?x=1', [System.UriKind]::Absolute)",
                Default("Uri"));
            Assert.Equal(
                "@{ ('alpha') = 1; ('endpoint') = [System.Uri]::new('relative/path', [System.UriKind]::Relative) }",
                Default("Dictionary"));
            Assert.True(string.IsNullOrEmpty(Default("CyclicCollection")));
            Assert.Equal(
                "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue(1, [int[]]@(0, 0)); $array.SetValue(2, [int[]]@(0, 1)); $array.SetValue(3, [int[]]@(1, 0)); $array.SetValue(4, [int[]]@(1, 1)); Write-Output -NoEnumerate $array }",
                Default("Matrix"));
            Assert.Equal(
                "[System.DateOnly]::FromDayNumber(([int]739827))",
                Default("DateOnly"));
            Assert.Equal(
                "[System.TimeOnly]::new(([long]452961234567))",
                Default("TimeOnly"));
            Assert.Equal(
                "[System.DateTime]::new(([long]639210116961234567), [System.DateTimeKind]::Local)",
                Default("DateTime"));
            Assert.Equal(
                "[System.DateTimeOffset]::ParseExact('2026-07-30T12:34:56.1234567+05:30', 'O', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)",
                Default("DateTimeOffset"));
            Assert.Equal(
                "[System.TimeSpan]::ParseExact('1.02:03:04.5678900', 'c', [System.Globalization.CultureInfo]::InvariantCulture)",
                Default("TimeSpan"));
            Assert.Equal(
                "[scriptblock]::Create((-join @('1+2', ([char]10), '```')))",
                Default("Script"));

            string Default(string name)
                => Assert.Single(command.Parameters, parameter => parameter.Name == name).DefaultValue;
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
}
