using System.Text;

namespace PowerForge.Tests;

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
param([string]$ManifestPath, [string]$SegmentExpression, [string]$UniqueExpression)
Import-Module -Name $ManifestPath -Force -ErrorAction Stop
$segment = & ([scriptblock]::Create($SegmentExpression))
if ($segment.FullName -cne 'N.1Bad') { throw 'Invalid-segment type did not round-trip.' }
$unique = & ([scriptblock]::Create($UniqueExpression))
if ($unique.FullName -cne 'N.Target-Type') { throw 'Duplicate-assembly type did not round-trip.' }
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);
            var invalidSegment = Default("InvalidSegmentType");
            var uniqueDuplicate = Default("UniqueDuplicateType");

            Assert.StartsWith("& { $assembly = ", invalidSegment, StringComparison.Ordinal);
            Assert.DoesNotContain("[N.1Bad]", invalidSegment, StringComparison.Ordinal);
            Assert.StartsWith("& { $assembly = ", uniqueDuplicate, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(Default("AmbiguousDuplicateType")));

            var execution = new PowerShellRunner().Run(new PowerShellRunRequest(
                evaluatorPath,
                new[] { manifestPath, invalidSegment, uniqueDuplicate },
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
}
