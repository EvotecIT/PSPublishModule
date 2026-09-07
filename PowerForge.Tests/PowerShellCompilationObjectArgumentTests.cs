using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("object", false)]
    [InlineData("int", true)]
    [InlineData("string", true)]
    public void ObjectArguments_RuntimeFreeLibrariesRequireClosedArgumentValues(string parameterType, bool supported)
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Value { param([" + parameterType + "]$Value) return [object]::ReferenceEquals($Value,$null) }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Equal(supported ? 1 : 0, typed.Methods.Length);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ObjectArguments_RetainUnqualifiedRuntimeOverloadSelection()
    {
        using var fixture = ArtifactFixture.Create("""
            function Add-UnknownValue {
                [CmdletBinding()] param([object[]]$Items, [System.Text.StringBuilder]$Builder)
                foreach($Item in $Items) { $null = $Builder.Append($Item) }
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ObjectArgumentMethods", "net10.0");
        Assert.Empty(typed.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ObjectArguments_PreserveClrArgumentUnwrappingWithoutChangingArrayStorage(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Add-ObjectArguments {
                [CmdletBinding()] param([object[]]$Items, [Collections.Generic.List[object]]$Observed)
                foreach($Item in $Items) { $Observed.Add($Item) }
            }
            function Add-ConstructorArguments {
                [CmdletBinding()] param([object[]]$Items, [Collections.Generic.List[System.WeakReference]]$Observed)
                foreach($Item in $Items) { $Reference=[System.WeakReference]::new($Item); $Observed.Add($Reference) }
            }
            function Add-StaticArguments {
                [CmdletBinding()] param([object[]]$Items, [Collections.Generic.List[bool]]$Observed)
                foreach($Item in $Items) {
                    foreach($Other in $Items) {
                        $Same=[object]::ReferenceEquals($Item,$Other)
                        $Observed.Add($Same)
                    }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ObjectArguments", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            $ErrorActionPreference='Stop'
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections.Generic;
            using System.Management.Automation;
            using System.Management.Automation.Internal;
            public sealed class ObjectArgumentValues {
                public readonly object[] Items;
                public ObjectArgumentValues() {
                    object number=7;
                    object text="text";
                    var vector=new int[] {1,2};
                    var empty=new object[0];
                    var note=new PSObject();
                    note.Properties.Add(new PSNoteProperty("Label","kept"));
                    Items=new object[] {null,AutomationNull.Value,number,new PSObject(number),new PSObject(text),vector,new PSObject(vector),empty,note};
                }
                public static string Describe(object[] values) {
                    var shapes=new List<string>();
                    foreach(var value in values) {
                        shapes.Add(value==null ? "null" : Object.ReferenceEquals(value,AutomationNull.Value) ? "AutomationNull" :
                            value.GetType().FullName + ":" + value.ToString());
                    }
                    return String.Join("|",shapes.ToArray());
                }
                public static string DescribeReferences(List<WeakReference> values) {
                    var targets=new List<object>();
                    foreach(var value in values) targets.Add(value.Target);
                    return Describe(targets.ToArray());
                }
            }
            '@
            $values=[ObjectArgumentValues]::new()
            $before=[ObjectArgumentValues]::Describe($values.Items)
            $objects=[Collections.Generic.List[object]]::new()
            $references=[Collections.Generic.List[System.WeakReference]]::new()
            $same=[Collections.Generic.List[bool]]::new()
            Add-ObjectArguments -Items $values.Items -Observed $objects
            Add-ConstructorArguments -Items $values.Items -Observed $references
            Add-StaticArguments -Items $values.Items -Observed $same
            [pscustomobject]@{
                before=$before
                objects=[ObjectArgumentValues]::Describe($objects.ToArray())
                constructors=[ObjectArgumentValues]::DescribeReferences($references)
                same=$same.ToArray()
                after=[ObjectArgumentValues]::Describe($values.Items)
            } | ConvertTo-Json -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-object-arguments");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-object-arguments");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("AutomationNull", original.StandardOutput, StringComparison.Ordinal);
        using var observations = System.Text.Json.JsonDocument.Parse(original.StandardOutput);
        Assert.Equal(81, observations.RootElement.GetProperty("same").GetArrayLength());
        Assert.Equal(observations.RootElement.GetProperty("before").GetString(), observations.RootElement.GetProperty("after").GetString());
        Assert.Equal(observations.RootElement.GetProperty("objects").GetString(), observations.RootElement.GetProperty("constructors").GetString());
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
