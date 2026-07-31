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
if (-not ('DefaultLiteralFixture.CaseMode' -as [type])) {
    Add-Type -TypeDefinition 'namespace DefaultLiteralFixture { public enum CaseMode { A = 1, a = 2 } }'
}
if (-not ('DefaultLiteralFixture.WeirdMode' -as [type])) {
    $assemblyName = [System.Reflection.AssemblyName]::new('DefaultLiteralFixtureDynamic')
    $assemblyBuilder = [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
        $assemblyName,
        [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    $moduleBuilder = $assemblyBuilder.DefineDynamicModule('DefaultLiteralFixtureDynamic')
    $enumBuilder = $moduleBuilder.DefineEnum(
        'DefaultLiteralFixture.WeirdMode',
        [System.Reflection.TypeAttributes]::Public,
        [int])
    [void]$enumBuilder.DefineLiteral('A-B', 1)
    [void]$enumBuilder.CreateTypeInfo()
    $unsafeTypeBuilder = $moduleBuilder.DefineEnum(
        'DefaultLiteralFixture.A-B',
        [System.Reflection.TypeAttributes]::Public,
        [int])
    [void]$unsafeTypeBuilder.DefineLiteral('X', 1)
    $script:unsafeDefaultType = $unsafeTypeBuilder.CreateTypeInfo().AsType()
    $whitespaceTypeBuilder = $moduleBuilder.DefineType(
        ' DefaultLiteralFixture.Edge ',
        [System.Reflection.TypeAttributes]::Public)
    $script:whitespaceDefaultType = $whitespaceTypeBuilder.CreateTypeInfo().AsType()
    $xmlInvalidTypeBuilder = $moduleBuilder.DefineType(
        ('DefaultLiteralFixture.Control' + [char]1),
        [System.Reflection.TypeAttributes]::Public)
    $script:xmlInvalidDefaultType = $xmlInvalidTypeBuilder.CreateTypeInfo().AsType()
}
if ($null -eq $script:unsafeDefaultType) {
    $script:unsafeDefaultType = [System.AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetType('DefaultLiteralFixture.A-B', $false, $false) } |
        Where-Object { $null -ne $_ } |
        Select-Object -First 1
}
if ($null -eq $script:whitespaceDefaultType) {
    $script:whitespaceDefaultType = [System.AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetTypes() } |
        Where-Object { $_.FullName -ceq ' DefaultLiteralFixture.Edge ' } |
        Select-Object -First 1
}
if ($null -eq $script:xmlInvalidDefaultType) {
    $xmlInvalidTypeName = 'DefaultLiteralFixture.Control' + [char]1
    $script:xmlInvalidDefaultType = [System.AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetTypes() } |
        Where-Object { $_.FullName -ceq $xmlInvalidTypeName } |
        Select-Object -First 1
}

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

        $decimalAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $decimalDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $decimalDefault.Value = [decimal]::Parse('0.1234567890123456789012345678', [System.Globalization.CultureInfo]::InvariantCulture)
        $decimalAttributes.Add($decimalDefault)
        $parameters.Add('PreciseDecimal', [System.Management.Automation.RuntimeDefinedParameter]::new('PreciseDecimal', [decimal], $decimalAttributes))

        $negativeDecimalAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $negativeDecimalDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $negativeDecimalDefault.Value = [decimal]::new(0, 0, 0, $true, ([byte]4))
        $negativeDecimalAttributes.Add($negativeDecimalDefault)
        $parameters.Add('NegativeDecimal', [System.Management.Automation.RuntimeDefinedParameter]::new('NegativeDecimal', [decimal], $negativeDecimalAttributes))

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
        $parameters.Add('Dictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('Dictionary', [System.Collections.IDictionary], $dictionaryAttributes))

        $caseDistinctDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $caseDistinctDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $caseDistinctDictionary = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
        $caseDistinctDictionary.Add('A', 1)
        $caseDistinctDictionary.Add('a', 2)
        $caseDistinctDictionaryDefault.Value = $caseDistinctDictionary
        $caseDistinctDictionaryAttributes.Add($caseDistinctDictionaryDefault)
        $parameters.Add('CaseDistinctDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('CaseDistinctDictionary', [System.Collections.Generic.Dictionary[string, int]], $caseDistinctDictionaryAttributes))

        $concurrentDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $concurrentDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $concurrentDictionary = [System.Collections.Concurrent.ConcurrentDictionary[string, int]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $concurrentDictionary['Alpha'] = 1
        $concurrentDictionaryDefault.Value = $concurrentDictionary
        $concurrentDictionaryAttributes.Add($concurrentDictionaryDefault)
        $parameters.Add('ConcurrentDictionary', [System.Management.Automation.RuntimeDefinedParameter]::new('ConcurrentDictionary', [System.Collections.Concurrent.ConcurrentDictionary[string, int]], $concurrentDictionaryAttributes))

        $cultureDictionaryAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $cultureDictionaryDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $cultureComparer = [System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true)
        $cultureDictionary = [System.Collections.Generic.Dictionary[string, int]]::new($cultureComparer)
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
        $parameters.Add('UnsupportedCulture', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsupportedCulture', [System.Globalization.CultureInfo], $unsupportedCultureAttributes))

        $unsupportedAddressAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $unsupportedAddressDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $unsupportedAddressDefault.Value = [System.Net.IPAddress]::Parse('192.0.2.1')
        $unsupportedAddressAttributes.Add($unsupportedAddressDefault)
        $parameters.Add('UnsupportedAddress', [System.Management.Automation.RuntimeDefinedParameter]::new('UnsupportedAddress', [System.Net.IPAddress], $unsupportedAddressAttributes))

        $cyclicAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $cyclicDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $cyclicValue = [System.Collections.ArrayList]::new()
        [void]$cyclicValue.Add($cyclicValue)
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

        $whitespaceTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $whitespaceTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $whitespaceTypeDefault.Value = $script:whitespaceDefaultType
        $whitespaceTypeAttributes.Add($whitespaceTypeDefault)
        $parameters.Add('WhitespaceType', [System.Management.Automation.RuntimeDefinedParameter]::new('WhitespaceType', [type], $whitespaceTypeAttributes))

        $xmlInvalidTypeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $xmlInvalidTypeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $xmlInvalidTypeDefault.Value = $script:xmlInvalidDefaultType
        $xmlInvalidTypeAttributes.Add($xmlInvalidTypeDefault)
        $parameters.Add('XmlInvalidType', [System.Management.Automation.RuntimeDefinedParameter]::new('XmlInvalidType', [type], $xmlInvalidTypeAttributes))

        $largeIntPtrAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $largeIntPtrDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $largeIntPtrDefault.Value = if ([System.IntPtr]::Size -eq 8) { [System.IntPtr]::new(([long]2147483648)) } else { [System.IntPtr]::new(([long]2147483647)) }
        $largeIntPtrAttributes.Add($largeIntPtrDefault)
        $parameters.Add('LargeIntPtr', [System.Management.Automation.RuntimeDefinedParameter]::new('LargeIntPtr', [System.IntPtr], $largeIntPtrAttributes))

        $largeUIntPtrAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $largeUIntPtrDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $largeUIntPtrDefault.Value = if ([System.IntPtr]::Size -eq 8) { [System.UIntPtr]::new(([uint64]4294967296)) } else { [System.UIntPtr]::new(([uint32]4294967295)) }
        $largeUIntPtrAttributes.Add($largeUIntPtrDefault)
        $parameters.Add('LargeUIntPtr', [System.Management.Automation.RuntimeDefinedParameter]::new('LargeUIntPtr', [System.UIntPtr], $largeUIntPtrAttributes))

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

        $statefulScriptAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $statefulScriptDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $capturedValue = 42
        $statefulScriptDefault.Value = { $capturedValue }.GetNewClosure()
        $statefulScriptAttributes.Add($statefulScriptDefault)
        $parameters.Add('StatefulScript', [System.Management.Automation.RuntimeDefinedParameter]::new('StatefulScript', [scriptblock], $statefulScriptAttributes))

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

        $caseModeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $caseModeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $caseModeDefault.Value = [System.Enum]::ToObject([DefaultLiteralFixture.CaseMode], 1)
        $caseModeAttributes.Add($caseModeDefault)
        $parameters.Add('CaseMode', [System.Management.Automation.RuntimeDefinedParameter]::new('CaseMode', [DefaultLiteralFixture.CaseMode], $caseModeAttributes))

        $weirdModeType = 'DefaultLiteralFixture.WeirdMode' -as [type]
        $weirdModeAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $weirdModeDefault = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $weirdModeDefault.Value = [System.Enum]::ToObject($weirdModeType, 1)
        $weirdModeAttributes.Add($weirdModeDefault)
        $parameters.Add('WeirdMode', [System.Management.Automation.RuntimeDefinedParameter]::new('WeirdMode', $weirdModeType, $weirdModeAttributes))

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

            Assert.Equal("([double]-0.0)", Default("NegativeDouble"));
            Assert.Equal(
                "[System.BitConverter]::Int64BitsToDouble(([long]9221120237041095220))",
                Default("PayloadDoubleNaN"));
            Assert.Equal("([double]1)", Default("IntegralDouble"));
            Assert.Equal("([single]-0.0)", Default("NegativeSingle"));
            Assert.Equal(
                "[System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]2143294004)), 0)",
                Default("PayloadSingleNaN"));
            Assert.Equal(
                "[System.Decimal]::new(([int]-1103563954), ([int]1836677777), ([int]66926059), $false, ([byte]28))",
                Default("PreciseDecimal"));
            Assert.Equal(
                "[System.Decimal]::new(([int]0), ([int]0), ([int]0), $true, ([byte]4))",
                Default("NegativeDecimal"));
            Assert.Equal(
                "[System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)",
                Default("BigInteger"));
            Assert.Equal(
                "[System.Management.Automation.SwitchParameter]::new($true)",
                Default("SwitchValue"));
            Assert.Equal(
                "[System.Guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')",
                Default("Guid"));
            Assert.Equal(
                "[System.Version]::Parse('1.2.3.4')",
                Default("Version"));
            Assert.Equal(
                "[System.Uri]::new('https://example.com/a''b?x=1', [System.UriKind]::Absolute)",
                Default("Uri"));
            Assert.True(string.IsNullOrEmpty(Default("UserEscapedUri")));
            Assert.Equal(
                "& { $dictionary = [System.Collections.Specialized.OrderedDictionary]::new([System.StringComparer]::OrdinalIgnoreCase); ([System.Collections.IDictionary]$dictionary).Add(('alpha'), (1)); ([System.Collections.IDictionary]$dictionary).Add(('endpoint'), ([System.Uri]::new('relative/path', [System.UriKind]::Relative))); return ,$dictionary }",
                Default("Dictionary"));
            Assert.Equal(
                "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new([System.StringComparer]::Ordinal); ([System.Collections.IDictionary]$dictionary).Add(('A'), (1)); ([System.Collections.IDictionary]$dictionary).Add(('a'), (2)); return ,$dictionary }",
                Default("CaseDistinctDictionary"));
            Assert.Equal(
                "& { $dictionary = [System.Collections.Concurrent.ConcurrentDictionary[System.String,System.Int32]]::new([System.StringComparer]::OrdinalIgnoreCase); ([System.Collections.IDictionary]$dictionary).Add(('Alpha'), (1)); return ,$dictionary }",
                Default("ConcurrentDictionary"));
            Assert.Equal(
                "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Int32]]::new([System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true)); ([System.Collections.IDictionary]$dictionary).Add(('I'), (1)); return ,$dictionary }",
                Default("CultureDictionary"));
            Assert.True(string.IsNullOrEmpty(Default("HybridDictionary")));
            Assert.True(string.IsNullOrEmpty(Default("ReadOnlyDictionary")));
            Assert.True(string.IsNullOrEmpty(Default("ReadOnlyOrderedDictionary")));
            Assert.True(string.IsNullOrEmpty(Default("UnsupportedCulture")));
            Assert.Equal(
                new[] { "One", "Two" },
                Assert.Single(command.Parameters, parameter => parameter.Name == "UnsupportedCulture").PossibleValues);
            Assert.True(string.IsNullOrEmpty(Default("UnsupportedAddress")));
            Assert.True(string.IsNullOrEmpty(Default("CyclicCollection")));
            Assert.True(string.IsNullOrEmpty(Default("SharedCollection")));
            Assert.Equal(
                "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue((1), [int[]]@(0, 0)); $array.SetValue((2), [int[]]@(0, 1)); $array.SetValue((3), [int[]]@(1, 0)); $array.SetValue((4), [int[]]@(1, 1)); return ,$array }",
                Default("Matrix"));
            Assert.Equal(
                "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2), [int[]]@(5)); $array.SetValue((7), [int[]]@(5)); $array.SetValue((8), [int[]]@(6)); return ,$array }",
                Default("BoundedArray"));
            Assert.Equal(
                "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $collection = [System.Int32[]]::new(2); $collection.SetValue((1), 0); $collection.SetValue((2), 1); return ,$collection }), 0); return ,$collection }",
                Default("NestedCollection"));
            Assert.Equal(
                "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue((1), [int[]]@(0, 0)); $array.SetValue((2), [int[]]@(0, 1)); $array.SetValue((3), [int[]]@(1, 0)); $array.SetValue((4), [int[]]@(1, 1)); return ,$array }), 0); return ,$collection }",
                Default("NestedMatrix"));
            Assert.True(string.IsNullOrEmpty(Default("Stack")));
            Assert.True(string.IsNullOrEmpty(Default("StatefulList")));
            Assert.StartsWith(
                "& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'DefaultLiteralFixtureDynamic",
                Default("UnsafeType"),
                StringComparison.Ordinal);
            Assert.Contains(
                "Where-Object { $_.FullName -ceq ' DefaultLiteralFixture.Edge ' }",
                Default("WhitespaceType"),
                StringComparison.Ordinal);
            Assert.DoesNotContain('\u0001', Default("XmlInvalidType"));
            Assert.Contains("([char]1)", Default("XmlInvalidType"), StringComparison.Ordinal);
            if (Environment.Is64BitProcess)
            {
                Assert.True(string.IsNullOrEmpty(Default("LargeIntPtr")));
                Assert.True(string.IsNullOrEmpty(Default("LargeUIntPtr")));
            }
            else
            {
                Assert.Equal("[System.IntPtr]::new(([System.Int64]2147483647))", Default("LargeIntPtr"));
                Assert.Equal("[System.UIntPtr]::new(([System.UInt64]4294967295))", Default("LargeUIntPtr"));
            }
            Assert.StartsWith(
                "& { $collection = [System.Activator]::CreateInstance(([System.Collections.Generic.List`1].MakeGenericType([type[]]@((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies()",
                Default("UnsafeList"),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "& { $dictionary = [System.Activator]::CreateInstance(([System.Collections.Generic.Dictionary`2].MakeGenericType([type[]]@((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies()",
                Default("UnsafeDictionary"),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "(& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'DefaultLiteralFixtureDynamic",
                Default("UnsafePointerType"),
                StringComparison.Ordinal);
            Assert.Contains(").MakePointerType()", Default("UnsafePointerType"), StringComparison.Ordinal);
            Assert.StartsWith(
                "& { $collection = [System.Array]::CreateInstance((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'DefaultLiteralFixtureDynamic",
                Default("UnsafeArray"),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "[System.Enum]::ToObject((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq 'DefaultLiteralFixtureDynamic",
                Default("UnsafeEnum"),
                StringComparison.Ordinal);
            Assert.Equal(
                "[System.DateOnly]::FromDayNumber(([int]739827))",
                Default("DateOnly"));
            Assert.Equal(
                "[System.TimeOnly]::new(([long]452961234567))",
                Default("TimeOnly"));
            var localDateTime = new DateTime(639210116961234567, DateTimeKind.Local);
            Assert.Equal(
                $"[System.DateTime]::new(([long]{localDateTime.Ticks}), [System.DateTimeKind]::Local)",
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
            Assert.True(string.IsNullOrEmpty(Default("StatefulScript")));
            Assert.Equal("[System.Int32].MakePointerType()", Default("PointerType"));
            Assert.Equal("[System.Int32].MakeByRefType()", Default("ByRefType"));
            Assert.Equal("[System.Int32].MakeArrayType(1)", Default("NonSzArrayType"));
            Assert.True(string.IsNullOrEmpty(Default("GenericParameterType")));
            Assert.Equal(
                "[System.Enum]::ToObject([DefaultLiteralFixture.CaseMode], ([System.Int32]1))",
                Default("CaseMode"));
            Assert.Equal(
                "[System.Enum]::ToObject([DefaultLiteralFixture.WeirdMode], ([System.Int32]1))",
                Default("WeirdMode"));

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
