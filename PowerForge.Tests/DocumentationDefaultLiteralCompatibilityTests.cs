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

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);

            Assert.Equal("-0.0", Default("NegativeDouble"));
            Assert.Equal("([single]-0.0)", Default("NegativeSingle"));
            Assert.Equal(
                "[System.Decimal]::Parse('0.1234567890123456789012345678', [System.Globalization.CultureInfo]::InvariantCulture)",
                Default("PreciseDecimal"));
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
