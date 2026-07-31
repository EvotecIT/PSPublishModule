using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationTypeIdentityCompatibilityTests
{
    [Fact]
    public void DocumentationEngine_UsesExactUnambiguousLoadedTypeIdentities()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-type-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "TypeIdentityFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'TypeIdentityFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '66666666-6666-6666-6666-666666666666'
    FunctionsToExport = @('Get-TypeIdentityFixture')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "TypeIdentityFixture.psm1"), """
function New-TypeIdentityAssembly([string]$name) {
    $assemblyName = [System.Reflection.AssemblyName]::new($name)
    $factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
        [System.Reflection.BindingFlags]'Public,Static') |
        Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
        Select-Object -First 1
    if ($factory) {
        return [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
            $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    }
    return [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
}

function Complete-TypeIdentityType([System.Reflection.Emit.TypeBuilder]$builder) {
    if ($builder.PSObject.Methods['CreateTypeInfo']) { return $builder.CreateTypeInfo().AsType() }
    return $builder.CreateType()
}

$segmentAssembly = New-TypeIdentityAssembly 'TypeIdentityFixtureSegments'
$segmentModule = $segmentAssembly.DefineDynamicModule('TypeIdentityFixtureSegments')
$script:invalidSegmentType = Complete-TypeIdentityType ($segmentModule.DefineType(
    'N.1Bad', [System.Reflection.TypeAttributes]::Public))
$unsafeGenericType = [System.Collections.Generic.List``1].MakeGenericType($script:invalidSegmentType)
$script:unsafeSzArrayType = $unsafeGenericType.MakeArrayType()
$script:unsafeMultidimensionalArrayType = $unsafeGenericType.MakeArrayType(2)

$uniqueFirstAssembly = New-TypeIdentityAssembly 'TypeIdentityFixtureDuplicateUnique'
$uniqueFirstModule = $uniqueFirstAssembly.DefineDynamicModule('TypeIdentityFixtureDuplicateUnique.First')
[void](Complete-TypeIdentityType ($uniqueFirstModule.DefineType('N.Decoy', [System.Reflection.TypeAttributes]::Public)))
$uniqueSecondAssembly = New-TypeIdentityAssembly 'TypeIdentityFixtureDuplicateUnique'
$uniqueSecondModule = $uniqueSecondAssembly.DefineDynamicModule('TypeIdentityFixtureDuplicateUnique.Second')
$script:uniqueDuplicateType = Complete-TypeIdentityType ($uniqueSecondModule.DefineType(
    'N.Target-Type', [System.Reflection.TypeAttributes]::Public))

$ambiguousFirstAssembly = New-TypeIdentityAssembly 'TypeIdentityFixtureDuplicateAmbiguous'
$ambiguousFirstModule = $ambiguousFirstAssembly.DefineDynamicModule('TypeIdentityFixtureDuplicateAmbiguous.First')
[void](Complete-TypeIdentityType ($ambiguousFirstModule.DefineType('N.Ambiguous-Type', [System.Reflection.TypeAttributes]::Public)))
$ambiguousSecondAssembly = New-TypeIdentityAssembly 'TypeIdentityFixtureDuplicateAmbiguous'
$ambiguousSecondModule = $ambiguousSecondAssembly.DefineDynamicModule('TypeIdentityFixtureDuplicateAmbiguous.Second')
$script:ambiguousDuplicateType = Complete-TypeIdentityType ($ambiguousSecondModule.DefineType(
    'N.Ambiguous-Type', [System.Reflection.TypeAttributes]::Public))

function Get-TypeIdentityFixture {
    [CmdletBinding()]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        foreach ($entry in @(
            @('InvalidSegmentType', $script:invalidSegmentType),
            @('UnsafeSzArrayType', $script:unsafeSzArrayType),
            @('UnsafeMultidimensionalArrayType', $script:unsafeMultidimensionalArrayType),
            @('UniqueDuplicateType', $script:uniqueDuplicateType),
            @('AmbiguousDuplicateType', $script:ambiguousDuplicateType)
        )) {
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $default.Value = $entry[1]
            $attributes.Add($default)
            $parameters.Add($entry[0], [System.Management.Automation.RuntimeDefinedParameter]::new(
                $entry[0], [type], $attributes))
        }
        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var evaluatorPath = Path.Combine(root, "EvaluateTypeIdentities.ps1");
            File.WriteAllText(evaluatorPath, """
param(
    [string]$ManifestPath,
    [string]$SegmentExpression,
    [string]$SzArrayExpression,
    [string]$MultidimensionalArrayExpression,
    [string]$UniqueExpression)
Import-Module -Name $ManifestPath -Force -ErrorAction Stop
$segment = & ([scriptblock]::Create($SegmentExpression))
if ($segment.FullName -cne 'N.1Bad') { throw 'Invalid-segment type did not round-trip.' }
$szArray = & ([scriptblock]::Create($SzArrayExpression))
if (-not $szArray.IsSZArray -or $szArray.GetElementType().GetGenericArguments()[0].FullName -cne 'N.1Bad') {
    throw 'Unsafe SZ array Type did not round-trip.'
}
$multidimensionalArray = & ([scriptblock]::Create($MultidimensionalArrayExpression))
if ($multidimensionalArray.GetArrayRank() -ne 2 -or
    $multidimensionalArray.GetElementType().GetGenericArguments()[0].FullName -cne 'N.1Bad') {
    throw 'Unsafe multidimensional array Type did not round-trip.'
}
$unique = & ([scriptblock]::Create($UniqueExpression))
if ($unique.FullName -cne 'N.Target-Type') { throw 'Duplicate-assembly type did not round-trip.' }
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);
            var invalidSegment = Default("InvalidSegmentType");
            var unsafeSzArray = Default("UnsafeSzArrayType");
            var unsafeMultidimensionalArray = Default("UnsafeMultidimensionalArrayType");
            var uniqueDuplicate = Default("UniqueDuplicateType");

            Assert.StartsWith("& { $assembly = ", invalidSegment, StringComparison.Ordinal);
            Assert.DoesNotContain("[N.1Bad]", invalidSegment, StringComparison.Ordinal);
            Assert.StartsWith("& { $assembly = ", uniqueDuplicate, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(Default("AmbiguousDuplicateType")));
            Assert.Contains(".MakeArrayType()", unsafeSzArray, StringComparison.Ordinal);
            Assert.Contains(".MakeArrayType(2)", unsafeMultidimensionalArray, StringComparison.Ordinal);

            var execution = new PowerShellRunner().Run(new PowerShellRunRequest(
                evaluatorPath,
                new[] { manifestPath, invalidSegment, unsafeSzArray, unsafeMultidimensionalArray, uniqueDuplicate },
                TimeSpan.FromMinutes(1)));
            Assert.True(execution.ExitCode == 0, execution.StdErr);

            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                payload,
                "TypeIdentityFixture",
                Path.Combine(root, "generated"));
            Assert.True(File.Exists(mamlPath));

            string Default(string name)
                => Assert.Single(command.Parameters, parameter => parameter.Name == name).DefaultValue;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    [Fact]
    public void DocumentationEngine_RejectsSpoofedScalarsAndStatefulCollectionBackingStores()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-runtime-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "RuntimeIdentityFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'RuntimeIdentityFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '55555555-5555-5555-5555-555555555555'
    FunctionsToExport = @('Get-RuntimeIdentityFixture')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "RuntimeIdentityFixture.psm1"), """
$spoofTypes = Add-Type -TypeDefinition @'
namespace System.Management.Automation {
    public sealed class SwitchParameter { public bool IsPresent { get { return true; } } }
}
namespace System.Numerics {
    public sealed class BigInteger : System.IFormattable {
        public string ToString(string format, System.IFormatProvider provider) { return "7"; }
    }
}
namespace System {
    public sealed class DateOnly { public int DayNumber { get { return 5; } } }
    public sealed class TimeOnly { public long Ticks { get { return 6L; } } }
    public sealed class TaggedUri : System.Uri {
        public TaggedUri() : base("https://example.com/", System.UriKind.Absolute) { }
        public string Tag { get { return "tag"; } }
    }
}
namespace RuntimeIdentityFixture {
    public sealed class TaggedTypeDelegator : System.Reflection.TypeDelegator {
        public TaggedTypeDelegator(System.Type type, string tag) : base(type) { Tag = tag; }
        public string Tag { get; private set; }
    }
    public sealed class ShapeSpoofingComparer : System.StringComparer {
        private readonly System.Globalization.CompareInfo _compareInfo =
            System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        private readonly bool _ignoreCase = true;
        public override int Compare(string x, string y) {
            return (x ?? string.Empty).Length.CompareTo((y ?? string.Empty).Length);
        }
        public override bool Equals(string x, string y) { return Compare(x, y) == 0; }
        public override int GetHashCode(string value) { return (value ?? string.Empty).Length; }
        public string State { get { return _compareInfo.Name + _ignoreCase.ToString(); } }
    }
}
'@ -PassThru

function New-SpoofedValue([string]$fullName) {
    $type = $spoofTypes | Where-Object { $_.FullName -ceq $fullName } | Select-Object -First 1
    return [System.Activator]::CreateInstance($type)
}

function New-RuntimeIdentityAssembly([string]$name) {
    $assemblyName = [System.Reflection.AssemblyName]::new($name)
    $factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
        [System.Reflection.BindingFlags]'Public,Static') |
        Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
        Select-Object -First 1
    if ($factory) {
        return [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
            $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    }
    return [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
}

function Complete-RuntimeIdentityType([System.Reflection.Emit.TypeBuilder]$builder) {
    if ($builder.PSObject.Methods['CreateTypeInfo']) { return $builder.CreateTypeInfo().AsType() }
    return $builder.CreateType()
}

function New-SpoofedGenericCollectionValue(
    [System.Reflection.Emit.ModuleBuilder]$module,
    [string]$fullName,
    [type]$frameworkDefinition) {
    $builder = $module.DefineType($fullName, [System.Reflection.TypeAttributes]::Public)
    $genericParameter = $builder.DefineGenericParameters([string[]]@('T'))[0]
    $builder.SetParent($frameworkDefinition.MakeGenericType([type[]]@($genericParameter)))
    [void]$builder.DefineDefaultConstructor([System.Reflection.MethodAttributes]::Public)
    [void]$builder.DefineField('Tag', [string], [System.Reflection.FieldAttributes]::Public)
    $definition = Complete-RuntimeIdentityType $builder
    $closedType = $definition.MakeGenericType([type[]]@([int]))
    $instance = [System.Activator]::CreateInstance($closedType)
    $closedType.GetField('Tag').SetValue($instance, 'tag')
    return ,$instance
}

$dictionaryAssembly = New-RuntimeIdentityAssembly 'RuntimeIdentityFixtureDictionary'
$dictionaryModule = $dictionaryAssembly.DefineDynamicModule('RuntimeIdentityFixtureDictionary')
$dictionaryBuilder = $dictionaryModule.DefineType(
    'System.Collections.Hashtable',
    [System.Reflection.TypeAttributes]'Public,Sealed',
    [System.Collections.Hashtable])
[void]$dictionaryBuilder.DefineDefaultConstructor([System.Reflection.MethodAttributes]::Public)
$tagField = $dictionaryBuilder.DefineField('Tag', [string], [System.Reflection.FieldAttributes]::Public)
$spoofHashtableType = Complete-RuntimeIdentityType $dictionaryBuilder
$script:spoofHashtable = [System.Activator]::CreateInstance($spoofHashtableType)
$spoofHashtableType.GetField('Tag').SetValue($script:spoofHashtable, 'tag')
$script:spoofList = New-SpoofedGenericCollectionValue -module $dictionaryModule -fullName 'System.Collections.Generic.List`1' -frameworkDefinition ([System.Collections.Generic.List``1])
$script:spoofCollection = New-SpoofedGenericCollectionValue -module $dictionaryModule -fullName 'System.Collections.ObjectModel.Collection`1' -frameworkDefinition ([System.Collections.ObjectModel.Collection``1])
$script:unorderedHashtable = [System.Collections.Hashtable]::new()
foreach ($index in 0..5) { $script:unorderedHashtable.Add(('key' + $index), $index) }

$script:statefulCollection = [System.Collections.ObjectModel.Collection[int]]::new(
    [System.Collections.Generic.IList[int]]([int[]](1, 2)))
$reservedBacking = [System.Collections.Generic.List[int]]::new(100)
$reservedBacking.Add(1)
$reservedBacking.Add(2)
$script:reservedBackingCollection = [System.Collections.ObjectModel.Collection[int]]::new($reservedBacking)
$script:itemOnlyCollection = [System.Collections.ObjectModel.Collection[int]]::new()
$script:itemOnlyCollection.Add(1)
$script:itemOnlyCollection.Add(2)
$script:reservedList = [System.Collections.Generic.List[int]]::new(100)
$script:reservedList.Add(1)
$script:reservedArrayList = [System.Collections.ArrayList]::new(100)
[void]$script:reservedArrayList.Add(1)
$script:invariantDictionary = [System.Collections.Generic.Dictionary[string,int]]::new(
    [System.StringComparer]::Create([System.Globalization.CultureInfo]::InvariantCulture, $true))
$script:invariantDictionary.Add('alpha', 1)
$shapeSpoofingComparerType = $spoofTypes |
    Where-Object { $_.FullName -ceq 'RuntimeIdentityFixture.ShapeSpoofingComparer' } |
    Select-Object -First 1
$shapeSpoofingComparer = [System.Activator]::CreateInstance($shapeSpoofingComparerType)
$script:shapeSpoofingDictionary = [System.Collections.Generic.Dictionary[string,int]]::new(
    [System.Collections.Generic.IEqualityComparer[string]]$shapeSpoofingComparer)
$script:shapeSpoofingDictionary.Add('a', 1)
$script:reservedSortedList = [System.Collections.Generic.SortedList[string,int]]::new(
    100, [System.StringComparer]::InvariantCultureIgnoreCase)
$script:reservedSortedList.Add('alpha', 1)
$typeDelegator = $spoofTypes |
    Where-Object { $_.FullName -ceq 'RuntimeIdentityFixture.TaggedTypeDelegator' } |
    Select-Object -First 1
function New-TaggedTypeDelegator([type]$type, [string]$tag) {
    return [System.Activator]::CreateInstance($typeDelegator, [object[]]@($type, $tag))
}
$script:delegatedArrayType = New-TaggedTypeDelegator ([string].MakeArrayType()) 'array'
$script:delegatedPointerType = New-TaggedTypeDelegator ([int].MakePointerType()) 'pointer'
$script:delegatedByRefType = New-TaggedTypeDelegator ([int].MakeByRefType()) 'byref'
$script:delegatedGenericType = New-TaggedTypeDelegator ([System.Collections.Generic.List[string]]) 'generic'

function Get-RuntimeIdentityFixture {
    [CmdletBinding()]
    [OutputType('A B', 'AB')]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        foreach ($entry in @(
            @('SpoofSwitch', (New-SpoofedValue 'System.Management.Automation.SwitchParameter')),
            @('SpoofBigInteger', (New-SpoofedValue 'System.Numerics.BigInteger')),
            @('SpoofDateOnly', (New-SpoofedValue 'System.DateOnly')),
            @('SpoofTimeOnly', (New-SpoofedValue 'System.TimeOnly')),
            @('TaggedUri', (New-SpoofedValue 'System.TaggedUri')),
            @('SpoofHashtable', $script:spoofHashtable),
            @('SpoofList', $script:spoofList),
            @('SpoofCollection', $script:spoofCollection),
            @('UnorderedHashtable', $script:unorderedHashtable),
            @('StatefulCollection', $script:statefulCollection),
            @('ReservedBackingCollection', $script:reservedBackingCollection),
            @('ItemOnlyCollection', $script:itemOnlyCollection),
            @('ReservedList', $script:reservedList),
            @('ReservedArrayList', $script:reservedArrayList),
            @('InvariantDictionary', $script:invariantDictionary),
            @('ShapeSpoofingDictionary', $script:shapeSpoofingDictionary),
            @('ReservedSortedList', $script:reservedSortedList),
            @('DelegatedArrayType', $script:delegatedArrayType),
            @('DelegatedPointerType', $script:delegatedPointerType),
            @('DelegatedByRefType', $script:delegatedByRefType),
            @('DelegatedGenericType', $script:delegatedGenericType)
        )) {
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $default.Value = $entry[1]
            $attributes.Add($default)
            $parameters.Add($entry[0], [System.Management.Automation.RuntimeDefinedParameter]::new(
                $entry[0], [object], $attributes))
        }
        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var evaluatorPath = Path.Combine(root, "EvaluateCollection.ps1");
            File.WriteAllText(evaluatorPath, """
param(
    [string]$ManifestPath,
    [string]$CollectionExpression,
    [string]$ListExpression,
    [string]$ArrayListExpression,
    [string]$DictionaryExpression,
    [string]$SortedListExpression)
Import-Module -Name $ManifestPath -Force -ErrorAction Stop
$collection = & ([scriptblock]::Create($CollectionExpression))
if ($collection.GetType() -ne [System.Collections.ObjectModel.Collection[int]]) {
    throw 'Collection type did not round-trip.'
}
$collection.Add(3)
if ($collection.Count -ne 3) { throw 'Collection mutability did not round-trip.' }
$list = & ([scriptblock]::Create($ListExpression))
if ($list.GetType() -ne [System.Collections.Generic.List[int]] -or $list.Capacity -ne 100) {
    throw 'List capacity did not round-trip.'
}
$arrayList = & ([scriptblock]::Create($ArrayListExpression))
if ($arrayList.GetType() -ne [System.Collections.ArrayList] -or $arrayList.Capacity -ne 100) {
    throw 'ArrayList capacity did not round-trip.'
}
$dictionary = & ([scriptblock]::Create($DictionaryExpression))
if (-not $dictionary.Comparer.Equals('ALPHA', 'alpha')) {
    throw 'Invariant dictionary comparer did not round-trip.'
}
$sortedList = & ([scriptblock]::Create($SortedListExpression))
if ($sortedList.GetType() -ne [System.Collections.Generic.SortedList[string,int]] -or
    $sortedList.Capacity -ne 100 -or
    $sortedList.Comparer.Compare('ALPHA', 'alpha') -ne 0) {
    throw 'SortedList capacity and comparer did not round-trip.'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var runner = new ExecutablePowerShellRunner(host, root);
                var payload = new DocumentationEngine(runner, new NullLogger())
                    .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(2));
                var command = Assert.Single(payload.Commands);

                Assert.True(string.IsNullOrEmpty(Default("SpoofSwitch")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofBigInteger")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofDateOnly")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofTimeOnly")));
                Assert.True(string.IsNullOrEmpty(Default("TaggedUri")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofHashtable")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofList")));
                Assert.True(string.IsNullOrEmpty(Default("SpoofCollection")));
                Assert.True(string.IsNullOrEmpty(Default("UnorderedHashtable")));
                Assert.True(string.IsNullOrEmpty(Default("StatefulCollection")));
                Assert.True(string.IsNullOrEmpty(Default("ReservedBackingCollection")));
                Assert.True(string.IsNullOrEmpty(Default("DelegatedArrayType")));
                Assert.True(string.IsNullOrEmpty(Default("DelegatedPointerType")));
                Assert.True(string.IsNullOrEmpty(Default("DelegatedByRefType")));
                Assert.True(string.IsNullOrEmpty(Default("DelegatedGenericType")));
                Assert.True(string.IsNullOrEmpty(Default("ShapeSpoofingDictionary")));
                var itemOnly = Default("ItemOnlyCollection");
                Assert.StartsWith("& { $collection = [System.Collections.ObjectModel.Collection[System.Int32]]::new()", itemOnly, StringComparison.Ordinal);
                var reservedList = Default("ReservedList");
                var reservedArrayList = Default("ReservedArrayList");
                var invariantDictionary = Default("InvariantDictionary");
                var reservedSortedList = Default("ReservedSortedList");
                Assert.Contains("::new(([int]100))", reservedList, StringComparison.Ordinal);
                Assert.Contains("::new(([int]100))", reservedArrayList, StringComparison.Ordinal);
                Assert.Contains("[System.StringComparer]::InvariantCultureIgnoreCase", invariantDictionary, StringComparison.Ordinal);
                Assert.Contains("::new(([int]100), [System.StringComparer]::InvariantCultureIgnoreCase)", reservedSortedList, StringComparison.Ordinal);
                Assert.Contains(command.Outputs, output => output.Name == "A B");
                Assert.Contains(command.Outputs, output => output.Name == "AB");

                var execution = runner.Run(new PowerShellRunRequest(
                    evaluatorPath,
                    new[] { manifestPath, itemOnly, reservedList, reservedArrayList, invariantDictionary, reservedSortedList },
                    TimeSpan.FromMinutes(1)));
                Assert.True(execution.ExitCode == 0, execution.StdErr);

                string Default(string name)
                    => Assert.Single(command.Parameters, parameter => parameter.Name == name).DefaultValue;
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
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
