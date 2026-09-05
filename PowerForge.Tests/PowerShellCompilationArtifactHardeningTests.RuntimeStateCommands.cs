using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Transpile_GetDateUsesRuntimeFreeProviderThroughBoundIr()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-CurrentLocalDate { $value = Microsoft.PowerShell.Utility\\Get-Date; return $value }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "runtime-state-get-date.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.True(function.Capabilities.HasFlag(PowerShellRequiredCapability.RuntimeStateIntrinsics));
        Assert.False(function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellHostTypes));
        Assert.False(function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellStreams));
        Assert.Equal(typeof(DateTime), Assert.Single(function.Locals).Type.ClrType);
        var lowered = Assert.IsType<PowerShellLoweredRuntimeStateExpression>(
            Assert.IsType<PowerShellLoweredAssignmentStatement>(Assert.Single(result.Lowered.Functions).Statements[0]).Value);
        Assert.Equal(PowerShellRuntimeStateIntrinsicKind.CurrentLocalDateTime, lowered.Kind);
        Assert.Equal("powerforge.command.runtime-state.get-date", lowered.Provider!.ProviderId);
        var method = Assert.Single(result.Emitted.Methods);
        Assert.Equal("powerforge.command.runtime-state.get-date", Assert.Single(method.CommandProviders).ProviderId);
        Assert.Contains("global::System.DateTime.Now", method.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", method.Source, StringComparison.Ordinal);
        Assert.False(method.RequiresPowerShellCommandRegions);
        Assert.False(method.RequiresPowerShellRuntimeState);
    }

    [Theory]
    [InlineData("Get-Date '2026-01-01'")]
    [InlineData("Get-Date -Date '2026-01-01'")]
    [InlineData("Get-Date -Format o")]
    [InlineData("Get-Date -UFormat '%s'")]
    [InlineData("Get-Date > 'proof.txt'")]
    [InlineData("& Get-Date")]
    public void Analyze_GetDateRejectsEveryWiderCommandShape(string command)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Get-Value {{ return {command} }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "runtime-state-get-date-rejected.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "command.get-date");
    }

    [Fact]
    public void Analyze_GetDateRejectsTargetWithoutRuntimeStateCapability()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Value { return Get-Date }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "runtime-state-get-date-no-capability.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapability.None);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "command.get-date");
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Build_StrictLibraryExecutesBoundedGetDateAcrossTargets(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create("function Get-CurrentLocalDate { return Get-Date }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, targetFramework),
            "PowerForge.RuntimeStateDate" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var provider = Assert.Single(result.Manifest.CommandProviders);
        Assert.Equal("powerforge.command.runtime-state.get-date", provider.ProviderId);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = Assert.Single(assembly.GetTypes().SelectMany(static type => type.GetMethods()), static method => method.Name == "Get_CurrentLocalDate");
        var before = DateTime.Now.AddSeconds(-2);
        var value = Assert.IsType<DateTime>(method.Invoke(null, null));
        var after = DateTime.Now.AddSeconds(2);

        Assert.Equal(DateTimeKind.Local, value.Kind);
        Assert.InRange(value, before, after);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModulePreservesQualifiedNoArgumentGetDateContract(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Get-CurrentLocalDate { [CmdletBinding()] param() return Microsoft.PowerShell.Utility\\Get-Date }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateDateModule" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("powerforge.command.runtime-state.get-date", Assert.Single(result.Manifest!.CommandProviders).ProviderId);
        const string proof = "$before=[DateTime]::Now.AddSeconds(-2); $value=Get-CurrentLocalDate; $after=[DateTime]::Now.AddSeconds(2); \"$($value.GetType().FullName)|$($value.Kind)|$([bool]($value -ge $before -and $value -le $after))\"";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {proof}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {proof}");

        Assert.Equal((0, "System.DateTime|Local|True"), (original.ExitCode, original.StandardOutput.Trim()));
        Assert.Equal((0, "System.DateTime|Local|True"), (compiled.ExitCode, compiled.StandardOutput.Trim()));
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Build_StrictNativeAotExecutableRunsBoundedGetDate()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        using var fixture = ArtifactFixture.Create(
            "$value = Get-Date; return [bool]($value.Kind -eq [System.DateTimeKind]::Local)");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeStateDateNativeAot",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            RuntimeIdentifier = "win-x64",
            SelfContained = true,
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot,
            TimeoutSeconds = 600
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(PowerShellCompilationDeploymentModel.NativeAot, result.Manifest!.TargetContract!.Deployment);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.Equal("powerforge.command.runtime-state.get-date", Assert.Single(result.Manifest.CommandProviders).ProviderId);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "True", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }
}
