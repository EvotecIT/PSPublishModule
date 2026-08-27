using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictBinaryModulePreservesArrayAndScalarConcatenation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ConcatenatedValues { return ([int[]](1, 2) + 3 + [int[]](4, 5)) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedArrayConcatenation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, RunModuleProof(result.ArtifactPath!, "Get-ConcatenatedValues").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesNullArrayConcatenation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullArrayValues { param([int[]] $Left, [int[]] $Right) " +
            "$first = $Left + 1; $second = [int[]](1, 2) + $Right; return @($first.Length, $first[0], $second.Length, $second[2]) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedNullArrayConcatenation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "1", "3" }, RunModuleProof(result.ArtifactPath!, "Get-NullArrayValues").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesArrayListIndexingAndMutation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-ListFlow { $list = [System.Collections.ArrayList]::new(); " +
            "$null = $list.Add('Ada'); $null = $list.Add('Grace'); $list[-1] = 'Linus'; " +
            "return @($list[0], $list[-1], $list.Count) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedListFlow",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "Ada", "Linus", "2" }, RunModuleProof(result.ArtifactPath!, "Invoke-ListFlow").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictBinaryModuleUsesOneKnownNotePropertyShapeForAllAccessForms()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-ObjectFlow { $item = [pscustomobject]@{ Name = 'Ada'; Count = 1 }; " +
            "$item.Name = 'Grace'; $item | Add-Member -NotePropertyName Status -NotePropertyValue 'Ready'; " +
            "return @($item.Name, $item.PSObject.Properties['Status'].Value, $item.Count) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedObjectFlow",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "Grace", "Ready", "1" }, RunModuleProof(result.ArtifactPath!, "Invoke-ObjectFlow").Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.DoesNotContain("Add-Member", generated, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PSObject.AsPSObject", generated, StringComparison.Ordinal);
    }
}
