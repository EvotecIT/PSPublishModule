using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridAdStyleModuleExecutesTypedWorkAroundHostedExternalCommand()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-AdStyle { [CmdletBinding()] param([int] $Value) [int] $before = $Value; $before += 1; Get-ADUser -Identity 'demo'; [int] $after = $before; $after += 1; return $after }",
            ".psm1");
        var manifestPath = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(manifestPath, "@{ RootModule='input.psm1'; ModuleVersion='1.0.0'; RequiredModules=@('ActiveDirectory'); FunctionsToExport=@('Invoke-AdStyle') }");
        var moduleRoot = Path.Combine(fixture.RootPath, "modules", "ActiveDirectory");
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, "ActiveDirectory.psm1"), "function Get-ADUser { param([string] $Identity) \"AD:$Identity\" }; Export-ModuleMember Get-ADUser");
        File.WriteAllText(Path.Combine(moduleRoot, "ActiveDirectory.psd1"), "@{ RootModule='ActiveDirectory.psm1'; ModuleVersion='1.0.0'; FunctionsToExport=@('Get-ADUser') }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "AdStyleProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifestPath
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.DependencyGraph!.Nodes, static node =>
            node.Identity.Name == "ActiveDirectory" && node.Disposition == PowerShellCompilationDependencyGraphDisposition.External);
        Assert.True(
            result.Manifest.CompiledMethods == 1,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var output = RunModuleProofWithModulePath(result.ArtifactPath!, "Invoke-AdStyle -Value 1", Path.Combine(fixture.RootPath, "modules"));
        Assert.Equal(new[] { "AD:demo", "3" }, output.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictLibraryExecutesInjectedRuntimeFreeStreamProviderWithoutSma()
    {
        using var fixture = ArtifactFixture.Create("function Write-NoticeProof { Write-Notice 'injected' }");
        var provider = new PowerShellCompilationCommandProviderContract
        {
            ProviderId = "tests.command.stream.notice",
            ProviderVersion = "1.0",
            FeatureId = "tests.command.write-notice",
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = "Write-Notice",
            Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "Message", Position = 0 } },
            Output = PowerShellCompilationCommandOutput.None,
            Cardinality = PowerShellCompilationCommandCardinality.None,
            Stream = "Information",
            Errors = PowerShellCompilationCommandErrors.None,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "WriteInformation",
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = true
            }
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "InjectedProviderProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            CommandProviders = new[] { provider }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        Assert.False(manifest.RequiresPowerShellRuntime);
        Assert.DoesNotContain(manifest.Dependencies, static dependency => dependency.Name.Contains("Management.Automation", StringComparison.OrdinalIgnoreCase));
        var recorded = Assert.Single(manifest.CommandProviders);
        Assert.Equal(provider.ProviderId, recorded.ProviderId);
        Assert.Equal("WriteInformation", recorded.Adapter.Operation);

        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetType("PowerForge.Compiled.InjectedProviderProofMethods", throwOnError: true)!.GetMethod("Write_NoticeProof")!;
        var information = new List<string>();
        method.Invoke(null, new object[]
        {
            (Action<object?>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(information.Add),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { })
        });
        Assert.Equal(new[] { "injected" }, information);
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesWriteHostInformationRecordIdentity()
    {
        using var fixture = ArtifactFixture.Create("function Write-HostProof { [CmdletBinding()] param() Write-Host -Object 'host-proof' }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "WriteHostProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var proof = RunModuleProof(
            result.ArtifactPath!,
            "$record = Write-HostProof 6>&1; \"$($record.Source)|$($record.Tags -join ',')|$($record.MessageData.GetType().FullName)|$($record.MessageData.Message)\"");

        Assert.Equal("Write-Host|PSHOST|System.Management.Automation.HostInformationMessage|host-proof", proof);
    }

    [Fact]
    public void Build_StrictBinaryModuleRecordsExactCommandProviderAndAdapterContracts()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SelectedValue { [CmdletBinding()] param([object[]] $InputObject) " +
            "$InputObject | Where-Object { $_ -ne $null } | Select-Object -First 1 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "CommandProviderProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        Assert.True(manifest.RequiresPowerShellRuntime);
        Assert.Equal(
            new[]
            {
                "powerforge.command.filtering.where-object",
                "powerforge.command.projection.select-object"
            },
            manifest.CommandProviders.Select(static provider => provider.ProviderId));
        Assert.All(manifest.CommandProviders, provider =>
        {
            Assert.Equal(1, provider.SchemaVersion);
            Assert.True(provider.CompileTimeOnly);
            Assert.False(provider.Adapter.RuntimeFree);
            Assert.Contains("System.Management.Automation", provider.Adapter.Dependencies);
        });
    }

    private static string RunModuleProofWithModulePath(string modulePath, string command, string additionalModulePath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["PSModulePath"] = additionalModulePath + Path.PathSeparator +
            (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty);
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Import-Module -Name '{modulePath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "AD-style module proof timed out.");
        Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return string.Join(Environment.NewLine, output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
