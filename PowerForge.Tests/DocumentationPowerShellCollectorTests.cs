using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
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
if (-not ('CollectorFixture.CaseMode' -as [type])) {
    Add-Type -TypeDefinition 'namespace CollectorFixture { public enum CaseMode { A = 1, a = 2 } public sealed class FixedComparerDictionary : System.Collections.Generic.Dictionary<string, int> { public FixedComparerDictionary() : base(System.StringComparer.Ordinal) { } } }'
}
if (-not ('CollectorFixture.WeirdMode' -as [type])) {
    $assemblyName = [System.Reflection.AssemblyName]::new('CollectorFixtureDynamic')
    $factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
        [System.Reflection.BindingFlags]'Public,Static') |
        Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
        Select-Object -First 1
    if ($factory) {
        $assemblyBuilder = [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
            $assemblyName,
            [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    } else {
        $assemblyBuilder = [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
            $assemblyName,
            [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    }
    $moduleBuilder = $assemblyBuilder.DefineDynamicModule('CollectorFixtureDynamic')
    $enumBuilder = $moduleBuilder.DefineEnum(
        'CollectorFixture.WeirdMode',
        [System.Reflection.TypeAttributes]::Public,
        [int])
    [void]$enumBuilder.DefineLiteral('A-B', 1)
    if ($enumBuilder.PSObject.Methods['CreateTypeInfo']) {
        [void]$enumBuilder.CreateTypeInfo()
    } else {
        [void]$enumBuilder.CreateType()
    }
    $unsafeEnumBuilder = $moduleBuilder.DefineEnum(
        'CollectorFixture.A-B',
        [System.Reflection.TypeAttributes]::Public,
        [int])
    [void]$unsafeEnumBuilder.DefineLiteral('X', 1)
    if ($unsafeEnumBuilder.PSObject.Methods['CreateTypeInfo']) {
        $script:unsafeDefaultType = $unsafeEnumBuilder.CreateTypeInfo().AsType()
    } else {
        $script:unsafeDefaultType = $unsafeEnumBuilder.CreateType()
    }
}
if ($null -eq $script:unsafeDefaultType) {
    $script:unsafeDefaultType = [System.AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetType('CollectorFixture.A-B', $false, $false) } |
        Where-Object { $null -ne $_ } |
        Select-Object -First 1
}

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

        $multilineHelpAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $multilineHelpDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $multilineHelpDefault.Help = "first`nsecond " + [char]0xD83D + [char]0xDE00
        $multilineHelpDefault.Value = 'ignored'
        $multilineHelpAttributes.Add($multilineHelpDefault)
        $parameters.Add('MultilineHelp', [System.Management.Automation.RuntimeDefinedParameter]::new('MultilineHelp', [string], $multilineHelpAttributes))

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

        $payloadDoubleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $payloadDoubleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $payloadDoubleDefault.Value = [System.BitConverter]::Int64BitsToDouble(([long]0x7ff8000000001234))
        $payloadDoubleAttributes.Add($payloadDoubleDefault)
        $parameters.Add('PayloadDoubleNaN', [System.Management.Automation.RuntimeDefinedParameter]::new('PayloadDoubleNaN', [double], $payloadDoubleAttributes))

        $integralDoubleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $integralDoubleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $integralDoubleDefault.Value = [double]1
        $integralDoubleAttributes.Add($integralDoubleDefault)
        $parameters.Add('IntegralDouble', [System.Management.Automation.RuntimeDefinedParameter]::new('IntegralDouble', [double], $integralDoubleAttributes))

        $negativeSingleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $negativeSingleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $negativeSingleDefault.Value = [System.BitConverter]::ToSingle([byte[]](0, 0, 0, 128), 0)
        $negativeSingleAttributes.Add($negativeSingleDefault)
        $parameters.Add('NegativeSingle', [System.Management.Automation.RuntimeDefinedParameter]::new('NegativeSingle', [single], $negativeSingleAttributes))

        $payloadSingleAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $payloadSingleDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $payloadSingleDefault.Value = [System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]0x7fc01234)), 0)
        $payloadSingleAttributes.Add($payloadSingleDefault)
        $parameters.Add('PayloadSingleNaN', [System.Management.Automation.RuntimeDefinedParameter]::new('PayloadSingleNaN', [single], $payloadSingleAttributes))

        $negativeDecimalAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $negativeDecimalDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $negativeDecimalDefault.Value = [decimal]::new(0, 0, 0, $true, ([byte]4))
        $negativeDecimalAttributes.Add($negativeDecimalDefault)
        $parameters.Add('NegativeDecimal', [System.Management.Automation.RuntimeDefinedParameter]::new('NegativeDecimal', [decimal], $negativeDecimalAttributes))

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

        $bigIntegerAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $bigIntegerDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $bigIntegerDefault.Value = [System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)
        $bigIntegerAttributes.Add($bigIntegerDefault)
        $parameters.Add('BigInteger', [System.Management.Automation.RuntimeDefinedParameter]::new('BigInteger', [System.Numerics.BigInteger], $bigIntegerAttributes))

        $switchAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $switchDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $switchDefault.Value = [System.Management.Automation.SwitchParameter]::new($true)
        $switchAttributes.Add($switchDefault)
        $parameters.Add('SwitchValue', [System.Management.Automation.RuntimeDefinedParameter]::new('SwitchValue', [System.Management.Automation.SwitchParameter], $switchAttributes))

        $pointerTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $pointerTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $pointerTypeDefault.Value = [int].MakePointerType()
        $pointerTypeAttributes.Add($pointerTypeDefault)
        $parameters.Add('PointerType', [System.Management.Automation.RuntimeDefinedParameter]::new('PointerType', [type], $pointerTypeAttributes))

        $byRefTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $byRefTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $byRefTypeDefault.Value = [int].MakeByRefType()
        $byRefTypeAttributes.Add($byRefTypeDefault)
        $parameters.Add('ByRefType', [System.Management.Automation.RuntimeDefinedParameter]::new('ByRefType', [type], $byRefTypeAttributes))

        $nonSzArrayTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $nonSzArrayTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $nonSzArrayTypeDefault.Value = [int].MakeArrayType(1)
        $nonSzArrayTypeAttributes.Add($nonSzArrayTypeDefault)
        $parameters.Add('NonSzArrayType', [System.Management.Automation.RuntimeDefinedParameter]::new('NonSzArrayType', [type], $nonSzArrayTypeAttributes))

        $genericParameterTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $genericParameterTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $genericParameterTypeDefault.Value = ([System.Collections.Generic.List[int]].GetGenericTypeDefinition()).GetGenericArguments()[0]
        $genericParameterTypeAttributes.Add($genericParameterTypeDefault)
        $parameters.Add('GenericParameterType', [System.Management.Automation.RuntimeDefinedParameter]::new('GenericParameterType', [type], $genericParameterTypeAttributes))

        $unsafeTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafeTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafeTypeDefault.Value = $script:unsafeDefaultType
        $unsafeTypeAttributes.Add($unsafeTypeDefault)
        $parameters.Add('UnsafeType', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafeType', [type], $unsafeTypeAttributes))

        $unsafeListAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafeListDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafeListType = [System.Collections.Generic.List``1].MakeGenericType($script:unsafeDefaultType)
        $unsafeListDefault.Value = [System.Activator]::CreateInstance($unsafeListType)
        $unsafeListAttributes.Add($unsafeListDefault)
        $parameters.Add('UnsafeList', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafeList', $unsafeListType, $unsafeListAttributes))

        $unsafeDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafeDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafeDictionaryType = [System.Collections.Generic.Dictionary``2].MakeGenericType([type[]]@($script:unsafeDefaultType, [int]))
        $unsafeDictionaryDefault.Value = [System.Activator]::CreateInstance($unsafeDictionaryType)
        $unsafeDictionaryAttributes.Add($unsafeDictionaryDefault)
        $parameters.Add('UnsafeDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafeDictionary', $unsafeDictionaryType, $unsafeDictionaryAttributes))

        $unsafePointerAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafePointerDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafePointerDefault.Value = $script:unsafeDefaultType.MakePointerType()
        $unsafePointerAttributes.Add($unsafePointerDefault)
        $parameters.Add('UnsafePointerType', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafePointerType', [type], $unsafePointerAttributes))

        $unsafeArrayAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafeArrayDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafeArrayDefault.Value = [System.Array]::CreateInstance($script:unsafeDefaultType, 1)
        $unsafeArrayAttributes.Add($unsafeArrayDefault)
        $parameters.Add('UnsafeArray', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafeArray', $script:unsafeDefaultType.MakeArrayType(), $unsafeArrayAttributes))

        $unsafeEnumAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsafeEnumDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsafeEnumDefault.Value = [System.Enum]::ToObject($script:unsafeDefaultType, 1)
        $unsafeEnumAttributes.Add($unsafeEnumDefault)
        $parameters.Add('UnsafeEnum', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsafeEnum', $script:unsafeDefaultType, $unsafeEnumAttributes))

        $caseModeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $caseModeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $caseModeDefault.Value = [System.Enum]::ToObject([CollectorFixture.CaseMode], 1)
        $caseModeAttributes.Add($caseModeDefault)
        $parameters.Add('CaseMode', [System.Management.Automation.RuntimeDefinedParameter]::new('CaseMode', [CollectorFixture.CaseMode], $caseModeAttributes))

        $weirdModeType = 'CollectorFixture.WeirdMode' -as [type]
        $weirdModeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $weirdModeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $weirdModeDefault.Value = [System.Enum]::ToObject($weirdModeType, 1)
        $weirdModeAttributes.Add($weirdModeDefault)
        $parameters.Add('WeirdMode', [System.Management.Automation.RuntimeDefinedParameter]::new('WeirdMode', $weirdModeType, $weirdModeAttributes))

        $uriAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $uriDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $uriDefault.Value = [uri]::new("https://example.com/a'b?x=1")
        $uriAttributes.Add($uriDefault)
        $parameters.Add('Uri', [System.Management.Automation.RuntimeDefinedParameter]::new('Uri', [uri], $uriAttributes))

        $userEscapedUriAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $userEscapedUriDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $userEscapedUriDefault.Value = [uri]::new('http://example.com/a%2Fb', $true)
        $userEscapedUriAttributes.Add($userEscapedUriDefault)
        $parameters.Add('UserEscapedUri', [System.Management.Automation.RuntimeDefinedParameter]::new('UserEscapedUri', [uri], $userEscapedUriAttributes))

        $dictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dictionaryDefault.Value = [ordered]@{
            alpha = 1
            endpoint = [uri]::new('relative/path', [System.UriKind]::Relative)
        }
        $dictionaryAttributes.Add($dictionaryDefault)
        $parameters.Add('Dictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('Dictionary', [object], $dictionaryAttributes))

        $caseDistinctDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $caseDistinctDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $caseDistinctDictionary = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
        $caseDistinctDictionary.Add('A', 1)
        $caseDistinctDictionary.Add('a', 2)
        $caseDistinctDictionaryDefault.Value = $caseDistinctDictionary
        $caseDistinctDictionaryAttributes.Add($caseDistinctDictionaryDefault)
        $parameters.Add('CaseDistinctDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('CaseDistinctDictionary', [System.Collections.Generic.Dictionary[string, int]], $caseDistinctDictionaryAttributes))

        $fixedComparerDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $fixedComparerDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $fixedComparerDictionary = [CollectorFixture.FixedComparerDictionary]::new()
        $fixedComparerDictionary['A'] = 1
        $fixedComparerDictionaryDefault.Value = $fixedComparerDictionary
        $fixedComparerDictionaryAttributes.Add($fixedComparerDictionaryDefault)
        $parameters.Add('FixedComparerDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('FixedComparerDictionary', [CollectorFixture.FixedComparerDictionary], $fixedComparerDictionaryAttributes))

        $concurrentDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $concurrentDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $concurrentDictionary = [System.Collections.Concurrent.ConcurrentDictionary[string, int]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $concurrentDictionary['Alpha'] = 1
        $concurrentDictionaryDefault.Value = $concurrentDictionary
        $concurrentDictionaryAttributes.Add($concurrentDictionaryDefault)
        $parameters.Add('ConcurrentDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('ConcurrentDictionary', [System.Collections.Concurrent.ConcurrentDictionary[string, int]], $concurrentDictionaryAttributes))

        $cultureDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $cultureDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $cultureDictionary = [System.Collections.Generic.Dictionary[string, int]]::new(
            [System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true))
        $cultureDictionary['I'] = 1
        $cultureDictionaryDefault.Value = $cultureDictionary
        $cultureDictionaryAttributes.Add($cultureDictionaryDefault)
        $parameters.Add('CultureDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('CultureDictionary', [System.Collections.Generic.Dictionary[string, int]], $cultureDictionaryAttributes))

        $hybridDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $hybridDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $hybridDictionary = [System.Collections.Specialized.HybridDictionary]::new($true)
        $hybridDictionary['A'] = 1
        $hybridDictionaryDefault.Value = $hybridDictionary
        $hybridDictionaryAttributes.Add($hybridDictionaryDefault)
        $parameters.Add('HybridDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('HybridDictionary', [System.Collections.Specialized.HybridDictionary], $hybridDictionaryAttributes))

        $readOnlyDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $readOnlyDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $readOnlyBacking = [System.Collections.Generic.Dictionary[string, int]]::new()
        $readOnlyBacking['alpha'] = 1
        $readOnlyDictionaryDefault.Value = [System.Collections.ObjectModel.ReadOnlyDictionary[string, int]]::new($readOnlyBacking)
        $readOnlyDictionaryAttributes.Add($readOnlyDictionaryDefault)
        $parameters.Add('ReadOnlyDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('ReadOnlyDictionary', [System.Collections.ObjectModel.ReadOnlyDictionary[string, int]], $readOnlyDictionaryAttributes))

        $readOnlyOrderedDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $readOnlyOrderedDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $readOnlyOrderedDictionary = [System.Collections.Specialized.OrderedDictionary]::new()
        $readOnlyOrderedDictionary.Add('alpha', 1)
        $readOnlyOrderedDictionaryDefault.Value = $readOnlyOrderedDictionary.AsReadOnly()
        $readOnlyOrderedDictionaryAttributes.Add($readOnlyOrderedDictionaryDefault)
        $parameters.Add('ReadOnlyOrderedDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('ReadOnlyOrderedDictionary', [System.Collections.Specialized.OrderedDictionary], $readOnlyOrderedDictionaryAttributes))

        $unsupportedCultureAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsupportedCultureDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsupportedCultureDefault.Value = [System.Globalization.CultureInfo]::new('en-US')
        $unsupportedCultureAttributes.Add($unsupportedCultureDefault)
        $unsupportedCultureAttributes.Add([System.Management.Automation.ValidateSetAttribute]::new([string[]]@('One', 'Two')))
        $parameters.Add('UnsupportedCulture', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsupportedCulture', [object], $unsupportedCultureAttributes))

        $unsupportedAddressAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsupportedAddressDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsupportedAddressDefault.Value = [System.Net.IPAddress]::Parse('192.0.2.1')
        $unsupportedAddressAttributes.Add($unsupportedAddressDefault)
        $parameters.Add('UnsupportedAddress', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsupportedAddress', [object], $unsupportedAddressAttributes))

        $cyclicAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $cyclicDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $cyclicValue = [System.Collections.ArrayList]::new()
        [void] $cyclicValue.Add($cyclicValue)
        $cyclicDefault.Value = $cyclicValue
        $cyclicAttributes.Add($cyclicDefault)
        $parameters.Add('CyclicCollection', [System.Management.Automation.RuntimeDefinedParameter]::new('CyclicCollection', [object], $cyclicAttributes))

        $sharedCollectionAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $sharedCollectionDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $sharedItem = [int[]](1, 2)
        $sharedCollection = [object[]]::new(2)
        $sharedCollection[0] = $sharedItem
        $sharedCollection[1] = $sharedItem
        $sharedCollectionDefault.Value = $sharedCollection
        $sharedCollectionAttributes.Add($sharedCollectionDefault)
        $parameters.Add('SharedCollection', [System.Management.Automation.RuntimeDefinedParameter]::new('SharedCollection', [object[]], $sharedCollectionAttributes))

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

        $boundedArrayAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $boundedArrayDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $boundedArray = [System.Array]::CreateInstance([int], [int[]]@(2), [int[]]@(5))
        $boundedArray.SetValue(7, 5)
        $boundedArray.SetValue(8, 6)
        $boundedArrayDefault.Value = $boundedArray
        $boundedArrayAttributes.Add($boundedArrayDefault)
        $parameters.Add('BoundedArray', [System.Management.Automation.RuntimeDefinedParameter]::new('BoundedArray', [System.Array], $boundedArrayAttributes))

        $nestedCollectionAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $nestedCollectionDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $nestedCollection = [object[]]::new(1)
        $nestedCollection[0] = [int[]](1, 2)
        $nestedCollectionDefault.Value = $nestedCollection
        $nestedCollectionAttributes.Add($nestedCollectionDefault)
        $parameters.Add('NestedCollection', [System.Management.Automation.RuntimeDefinedParameter]::new('NestedCollection', [object[]], $nestedCollectionAttributes))

        $nestedMatrixAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $nestedMatrixDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $nestedMatrix = [object[]]::new(1)
        $nestedMatrix[0] = $matrix
        $nestedMatrixDefault.Value = $nestedMatrix
        $nestedMatrixAttributes.Add($nestedMatrixDefault)
        $parameters.Add('NestedMatrix', [System.Management.Automation.RuntimeDefinedParameter]::new('NestedMatrix', [object[]], $nestedMatrixAttributes))

        $stackAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $stackDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $stack = [System.Collections.Generic.Stack[int]]::new()
        $stack.Push(1)
        $stack.Push(2)
        $stackDefault.Value = $stack
        $stackAttributes.Add($stackDefault)
        $parameters.Add('Stack', [System.Management.Automation.RuntimeDefinedParameter]::new('Stack', [System.Collections.Generic.Stack[int]], $stackAttributes))

        $statefulListAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $statefulListDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $statefulList = [System.ComponentModel.BindingList[int]]::new()
        $statefulList.Add(1)
        $statefulList.RaiseListChangedEvents = $false
        $statefulList.AllowEdit = $false
        $statefulListDefault.Value = $statefulList
        $statefulListAttributes.Add($statefulListDefault)
        $parameters.Add('StatefulList', [System.Management.Automation.RuntimeDefinedParameter]::new('StatefulList', [System.ComponentModel.BindingList[int]], $statefulListAttributes))

        $dateOnlyType = 'System.DateOnly' -as [type]
        if ($dateOnlyType) {
            $dateOnlyAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $dateOnlyDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $dateOnlyDefault.Value = $dateOnlyType.GetMethod('FromDayNumber').Invoke($null, [object[]]([int]739827))
            $dateOnlyAttributes.Add($dateOnlyDefault)
            $parameters.Add('DateOnly', [System.Management.Automation.RuntimeDefinedParameter]::new('DateOnly', $dateOnlyType, $dateOnlyAttributes))
        }

        $timeOnlyType = 'System.TimeOnly' -as [type]
        if ($timeOnlyType) {
            $timeOnlyAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $timeOnlyDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $timeOnlyDefault.Value = [System.Activator]::CreateInstance($timeOnlyType, [object[]]([long]452961234567))
            $timeOnlyAttributes.Add($timeOnlyDefault)
            $parameters.Add('TimeOnly', [System.Management.Automation.RuntimeDefinedParameter]::new('TimeOnly', $timeOnlyType, $timeOnlyAttributes))
        }

        $dateTimeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $dateTimeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $dateTimeDefault.Value = [datetime]::new(([long]639210116961234567), [System.DateTimeKind]::Local)
        $dateTimeAttributes.Add($dateTimeDefault)
        $parameters.Add('DateTime', [System.Management.Automation.RuntimeDefinedParameter]::new('DateTime', [datetime], $dateTimeAttributes))

        $statefulScriptAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $statefulScriptDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $capturedValue = 42
        $statefulScriptDefault.Value = { $capturedValue }.GetNewClosure()
        $statefulScriptAttributes.Add($statefulScriptDefault)
        $parameters.Add('StatefulScript', [System.Management.Automation.RuntimeDefinedParameter]::new('StatefulScript', [scriptblock], $statefulScriptAttributes))

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
                var multilineHelp = Assert.Single(command.Parameters, parameter => parameter.Name == "MultilineHelp");
                var invalidSurrogate = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "InvalidSurrogate");
                var invalidSurrogateHelp = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "InvalidSurrogateHelp");
                var invalidText = Assert.Single(command.Parameters, parameter => parameter.Name == "InvalidText");
                var longHelp = Assert.Single(command.Parameters, parameter => parameter.Name == "LongHelp");
                var negativeDouble = Assert.Single(command.Parameters, parameter => parameter.Name == "NegativeDouble");
                var payloadDouble = Assert.Single(command.Parameters, parameter => parameter.Name == "PayloadDoubleNaN");
                var integralDouble = Assert.Single(command.Parameters, parameter => parameter.Name == "IntegralDouble");
                var negativeSingle = Assert.Single(command.Parameters, parameter => parameter.Name == "NegativeSingle");
                var payloadSingle = Assert.Single(command.Parameters, parameter => parameter.Name == "PayloadSingleNaN");
                var negativeDecimal = Assert.Single(command.Parameters, parameter => parameter.Name == "NegativeDecimal");
                var guid = Assert.Single(command.Parameters, parameter => parameter.Name == "Guid");
                var version = Assert.Single(command.Parameters, parameter => parameter.Name == "Version");
                var bigInteger = Assert.Single(command.Parameters, parameter => parameter.Name == "BigInteger");
                var switchValue = Assert.Single(command.Parameters, parameter => parameter.Name == "SwitchValue");
                var pointerType = Assert.Single(command.Parameters, parameter => parameter.Name == "PointerType");
                var byRefType = Assert.Single(command.Parameters, parameter => parameter.Name == "ByRefType");
                var nonSzArrayType = Assert.Single(command.Parameters, parameter => parameter.Name == "NonSzArrayType");
                var genericParameterType = Assert.Single(command.Parameters, parameter => parameter.Name == "GenericParameterType");
                var unsafeType = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafeType");
                var unsafeList = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafeList");
                var unsafeDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafeDictionary");
                var unsafePointerType = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafePointerType");
                var unsafeArray = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafeArray");
                var unsafeEnum = Assert.Single(command.Parameters, parameter => parameter.Name == "UnsafeEnum");
                var caseMode = Assert.Single(command.Parameters, parameter => parameter.Name == "CaseMode");
                var weirdMode = Assert.Single(command.Parameters, parameter => parameter.Name == "WeirdMode");
                var uri = Assert.Single(command.Parameters, parameter => parameter.Name == "Uri");
                var userEscapedUri = Assert.Single(command.Parameters, parameter => parameter.Name == "UserEscapedUri");
                var dictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "Dictionary");
                var caseDistinctDictionary = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "CaseDistinctDictionary");
                var fixedComparerDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "FixedComparerDictionary");
                var concurrentDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "ConcurrentDictionary");
                var cultureDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "CultureDictionary");
                var hybridDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "HybridDictionary");
                var readOnlyDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "ReadOnlyDictionary");
                var readOnlyOrderedDictionary = Assert.Single(command.Parameters, parameter => parameter.Name == "ReadOnlyOrderedDictionary");
                var unsupportedCulture = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "UnsupportedCulture");
                var unsupportedAddress = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "UnsupportedAddress");
                var cyclicCollection = Assert.Single(
                    command.Parameters,
                    parameter => parameter.Name == "CyclicCollection");
                var sharedCollection = Assert.Single(command.Parameters, parameter => parameter.Name == "SharedCollection");
                var matrix = Assert.Single(command.Parameters, parameter => parameter.Name == "Matrix");
                var boundedArray = Assert.Single(command.Parameters, parameter => parameter.Name == "BoundedArray");
                var nestedCollection = Assert.Single(command.Parameters, parameter => parameter.Name == "NestedCollection");
                var nestedMatrix = Assert.Single(command.Parameters, parameter => parameter.Name == "NestedMatrix");
                var stack = Assert.Single(command.Parameters, parameter => parameter.Name == "Stack");
                var statefulList = Assert.Single(command.Parameters, parameter => parameter.Name == "StatefulList");
                var dateOnly = command.Parameters.SingleOrDefault(parameter => parameter.Name == "DateOnly");
                var timeOnly = command.Parameters.SingleOrDefault(parameter => parameter.Name == "TimeOnly");
                var dateTime = Assert.Single(command.Parameters, parameter => parameter.Name == "DateTime");
                var statefulScript = Assert.Single(command.Parameters, parameter => parameter.Name == "StatefulScript");
                var dateTimeOffset = Assert.Single(command.Parameters, parameter => parameter.Name == "DateTimeOffset");
                var timeSpan = Assert.Single(command.Parameters, parameter => parameter.Name == "TimeSpan");
                var accelerated = Assert.Single(
                    payload.Commands,
                    item => item.Name == "Get-AcceleratedOutput");
                var acceleratedOutput = Assert.Single(accelerated.Outputs);

                Assert.Equal(NestedExpression(80, "1"), nested.DefaultValue);
                Assert.Equal("authored display value", helpWins.DefaultValue);
                Assert.Equal("first\nsecond 😀", multilineHelp.DefaultValue);
                Assert.Equal("(-join @(([char]55296)))", invalidSurrogate.DefaultValue);
                Assert.Equal("([char]55296)", invalidSurrogateHelp.DefaultValue);
                Assert.True(string.IsNullOrEmpty(invalidText.DefaultValue));
                Assert.Equal(80000, longHelp.DefaultValue.Length);
                Assert.All(longHelp.DefaultValue, character => Assert.Equal('x', character));
                Assert.Equal("([double]-0.0)", negativeDouble.DefaultValue);
                Assert.Equal("[System.BitConverter]::Int64BitsToDouble(([long]9221120237041095220))", payloadDouble.DefaultValue);
                Assert.Equal("([double]1)", integralDouble.DefaultValue);
                Assert.Equal("([single]-0.0)", negativeSingle.DefaultValue);
                Assert.Equal("[System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]2143294004)), 0)", payloadSingle.DefaultValue);
                Assert.Equal("[System.Decimal]::new(([int]0), ([int]0), ([int]0), $true, ([byte]4))", negativeDecimal.DefaultValue);
                Assert.Equal(
                    "[System.Guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')",
                    guid.DefaultValue);
                Assert.Equal("[System.Version]::Parse('1.2.3.4')", version.DefaultValue);
                Assert.Equal(
                    "[System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)",
                    bigInteger.DefaultValue);
                Assert.Equal(
                    "[System.Management.Automation.SwitchParameter]::new($true)",
                    switchValue.DefaultValue);
                Assert.Equal("[System.Int32].MakePointerType()", pointerType.DefaultValue);
                Assert.Equal("[System.Int32].MakeByRefType()", byRefType.DefaultValue);
                Assert.Equal("[System.Int32].MakeArrayType(1)", nonSzArrayType.DefaultValue);
                Assert.True(string.IsNullOrEmpty(genericParameterType.DefaultValue));
                Assert.StartsWith(
                    "& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'CollectorFixtureDynamic",
                    unsafeType.DefaultValue,
                    StringComparison.Ordinal);
                Assert.StartsWith(
                    "& { $collection = [System.Activator]::CreateInstance(([System.Collections.Generic.List`1].MakeGenericType([type[]]@((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies()",
                    unsafeList.DefaultValue,
                    StringComparison.Ordinal);
                Assert.StartsWith(
                    "& { $dictionary = [System.Activator]::CreateInstance(([System.Collections.Generic.Dictionary`2].MakeGenericType([type[]]@((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies()",
                    unsafeDictionary.DefaultValue,
                    StringComparison.Ordinal);
                Assert.StartsWith(
                    "(& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'CollectorFixtureDynamic",
                    unsafePointerType.DefaultValue,
                    StringComparison.Ordinal);
                Assert.Contains(").MakePointerType()", unsafePointerType.DefaultValue, StringComparison.Ordinal);
                Assert.StartsWith(
                    "& { $collection = [System.Array]::CreateInstance((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'CollectorFixtureDynamic",
                    unsafeArray.DefaultValue,
                    StringComparison.Ordinal);
                Assert.StartsWith(
                    "[System.Enum]::ToObject((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'CollectorFixtureDynamic",
                    unsafeEnum.DefaultValue,
                    StringComparison.Ordinal);
                Assert.Equal(
                    "[System.Enum]::ToObject([CollectorFixture.CaseMode], ([System.Int32]1))",
                    caseMode.DefaultValue);
                Assert.Equal(
                    "[System.Enum]::ToObject([CollectorFixture.WeirdMode], ([System.Int32]1))",
                    weirdMode.DefaultValue);
                Assert.Equal(
                    "[System.Uri]::new('https://example.com/a''b?x=1', [System.UriKind]::Absolute)",
                    uri.DefaultValue);
                Assert.True(string.IsNullOrEmpty(userEscapedUri.DefaultValue));
                Assert.StartsWith(
                    "& { $dictionary = [System.Collections.Specialized.OrderedDictionary]::new(",
                    dictionary.DefaultValue,
                    StringComparison.Ordinal);
                Assert.Contains("([System.Collections.IDictionary]$dictionary).Add(('alpha'), (1))", dictionary.DefaultValue, StringComparison.Ordinal);
                Assert.Contains("([System.Collections.IDictionary]$dictionary).Add(('endpoint'), ([System.Uri]::new('relative/path', [System.UriKind]::Relative)))", dictionary.DefaultValue, StringComparison.Ordinal);
                Assert.Contains(
                    caseDistinctDictionary.DefaultValue,
                    new[]
                    {
                        "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new([System.StringComparer]::Ordinal); ([System.Collections.IDictionary]$dictionary).Add(('A'), (1)); ([System.Collections.IDictionary]$dictionary).Add(('a'), (2)); return ,$dictionary }",
                        "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new(([int]3), [System.StringComparer]::Ordinal); ([System.Collections.IDictionary]$dictionary).Add(('A'), (1)); ([System.Collections.IDictionary]$dictionary).Add(('a'), (2)); return ,$dictionary }"
                    });
                Assert.True(string.IsNullOrEmpty(fixedComparerDictionary.DefaultValue));
                Assert.Equal(
                    "& { $dictionary = [System.Collections.Concurrent.ConcurrentDictionary[System.String,System.Int32]]::new([System.StringComparer]::OrdinalIgnoreCase); ([System.Collections.IDictionary]$dictionary).Add(('Alpha'), (1)); return ,$dictionary }",
                    concurrentDictionary.DefaultValue);
                Assert.Contains(
                    cultureDictionary.DefaultValue,
                    new[]
                    {
                        "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new([System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true)); ([System.Collections.IDictionary]$dictionary).Add(('I'), (1)); return ,$dictionary }",
                        "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new(([int]3), [System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true)); ([System.Collections.IDictionary]$dictionary).Add(('I'), (1)); return ,$dictionary }"
                    });
                Assert.True(string.IsNullOrEmpty(hybridDictionary.DefaultValue));
                Assert.True(string.IsNullOrEmpty(readOnlyDictionary.DefaultValue));
                Assert.True(string.IsNullOrEmpty(readOnlyOrderedDictionary.DefaultValue));
                Assert.True(string.IsNullOrEmpty(unsupportedCulture.DefaultValue));
                Assert.Equal(new[] { "One", "Two" }, unsupportedCulture.PossibleValues);
                Assert.True(string.IsNullOrEmpty(unsupportedAddress.DefaultValue));
                Assert.True(string.IsNullOrEmpty(cyclicCollection.DefaultValue));
                Assert.True(string.IsNullOrEmpty(sharedCollection.DefaultValue));
                Assert.Equal(
                    "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue((1), [int[]]@(0, 0)); $array.SetValue((2), [int[]]@(0, 1)); $array.SetValue((3), [int[]]@(1, 0)); $array.SetValue((4), [int[]]@(1, 1)); return ,$array }",
                    matrix.DefaultValue);
                Assert.Equal(
                    "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2), [int[]]@(5)); $array.SetValue((7), [int[]]@(5)); $array.SetValue((8), [int[]]@(6)); return ,$array }",
                    boundedArray.DefaultValue);
                Assert.Equal(
                    "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $collection = [System.Int32[]]::new(2); $collection.SetValue((1), 0); $collection.SetValue((2), 1); return ,$collection }), 0); return ,$collection }",
                    nestedCollection.DefaultValue);
                Assert.Equal(
                    "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue((1), [int[]]@(0, 0)); $array.SetValue((2), [int[]]@(0, 1)); $array.SetValue((3), [int[]]@(1, 0)); $array.SetValue((4), [int[]]@(1, 1)); return ,$array }), 0); return ,$collection }",
                    nestedMatrix.DefaultValue);
                Assert.True(string.IsNullOrEmpty(stack.DefaultValue));
                Assert.True(string.IsNullOrEmpty(statefulList.DefaultValue));
                if (host.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.NotNull(dateOnly);
                    Assert.NotNull(timeOnly);
                    Assert.Equal("[System.DateOnly]::FromDayNumber(([int]739827))", dateOnly!.DefaultValue);
                    Assert.Equal("[System.TimeOnly]::new(([long]452961234567))", timeOnly!.DefaultValue);
                }
                else
                {
                    Assert.Null(dateOnly);
                    Assert.Null(timeOnly);
                }
                var localDateTime = new DateTime(639210116961234567, DateTimeKind.Local);
                Assert.Equal(
                    $"[System.DateTime]::new(([long]{localDateTime.Ticks}), [System.DateTimeKind]::Local)",
                    dateTime.DefaultValue);
                Assert.True(string.IsNullOrEmpty(statefulScript.DefaultValue));
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

}
