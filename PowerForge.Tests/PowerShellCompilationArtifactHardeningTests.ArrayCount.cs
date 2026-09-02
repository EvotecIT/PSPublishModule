using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void RuntimeFreeArrayCountUsesClrLengthWithPowerShellNullNormalization()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-ArrayCount { param([array] $Values) return $Values.Count }; " +
            "function Get-StringArrayCount { param([string[]] $Values) return $Values.cOuNt }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "array-count.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.Equal(2, result.Emitted.Methods.Length);
        Assert.All(result.Analyzed.Functions, static function =>
            Assert.False(function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellHostTypes)));
        foreach (var function in result.Analyzed.Functions)
        {
            var member = Assert.IsType<PowerShellBoundClrMemberExpression>(
                Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(function.Body.Statements)).Expression);
            Assert.Equal(nameof(Array.Length), member.MemberName);
            Assert.Equal(PowerShellClrReceiverBehavior.NormalizeNullCount, member.ReceiverBehavior);
            Assert.Equal(typeof(int), member.Type.ClrType);
        }
        Assert.All(result.Emitted.Methods, static method =>
        {
            Assert.Contains("?.Length ?? 0", method.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Management.Automation", method.Source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DynamicObjectCountDoesNotEnterTheRuntimeFreeArrayContract()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-DynamicCount { param([object] $Value) return $Value.Count }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "dynamic-count.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSB2602");
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeArrayCountExecutesAcrossTargets(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayCount { param([array] $Values) return $Values.Count }; " +
            "function Get-StringArrayCount { param([string[]] $Values) return $Values.Count }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, targetFramework),
            "PowerForge.ArrayCount" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var methods = assembly.GetTypes().SelectMany(static type => type.GetMethods()).ToArray();
        var arrayCount = Assert.Single(methods, static method => method.Name == "Get_ArrayCount");
        var stringArrayCount = Assert.Single(methods, static method => method.Name == "Get_StringArrayCount");

        Assert.Equal(0, arrayCount.Invoke(null, new object?[] { null }));
        Assert.Equal(0, arrayCount.Invoke(null, new object?[] { Array.Empty<object>() }));
        Assert.Equal(6, arrayCount.Invoke(null, new object?[] { new int[2, 3] }));
        Assert.Equal(0, stringArrayCount.Invoke(null, new object?[] { null }));
        Assert.Equal(0, stringArrayCount.Invoke(null, new object?[] { Array.Empty<string>() }));
        Assert.Equal(3, stringArrayCount.Invoke(null, new object?[] { new[] { "a", "b", "c" } }));
    }
}
