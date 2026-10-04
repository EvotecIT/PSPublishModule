using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(InterpolationScalarHosts))]
    public void Interpolation_MapsLiteralAndEvaluatedTokensWithoutDecodedTextSearch(string framework, string host, bool library)
    {
        var templates = new List<string>
        {
            "\"`$Value $Value\"", "\"`${Value} ${Value}\"", "\"`$Value/$Value/$Value\"",
            "\"``$Value\"", "\"{{$Value}}:{${Value}}\"", "\"line`n$Value`t`${Value} $Value\"",
            "@\"\ntext `$Value $Value ${Value}\n\"@"
        };
        if (framework != "net472") templates.AddRange(new[]
        {
            "\"`u{24}Value $Value\"", "\"`u{24}{Value} ${Value}\"", "\"`u{24}Value/$Value/$Value\""
        });
        var source = string.Join(Environment.NewLine, templates.Select((template, index) =>
            "function Expand-Case" + index + " {\n[CmdletBinding()] param([int]$Value)\nreturn " + template + "\n}"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.InterpolationMapping", library ? PowerShellCompilationArtifactKind.Library : PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(templates.Count, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        if (library) Assert.False(result.Manifest.RequiresPowerShellRuntime);
        var probe = "foreach($index in 0.." + (templates.Count - 1) + ") { Invoke-Case $index 123 }";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($index,$value) { & ('Expand-Case'+$index) $value }; " + probe,
            fixture.RootPath, "original-interpolation-mapping");
        var setup = library
            ? "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) + "'); " +
              "$type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Expand_Case0') }; " +
              "function Invoke-Case($index,$value) { $type.GetMethod('Expand_Case'+$index).Invoke($null,[object[]]@($value)) }; "
            : "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; function Invoke-Case($index,$value) { & ('Expand-Case'+$index) $value }; ";
        var compiled = RunStatementErrorProbe(host, setup + probe, fixture.RootPath, "compiled-interpolation-mapping");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Empty(compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
