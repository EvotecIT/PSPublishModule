using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictLibraryPublishesVersionedRuntimeFreeContractAndAbi()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ContractProof { param([Parameter(Mandatory)] [Alias('v')] [int] $Value) return $Value }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "ContractProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.NotNull(manifest.SemanticProfile);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeStrictName, manifest.SemanticProfile.Name);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion, manifest.SemanticProfile.Version);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeAbiVersion, manifest.SemanticProfile.CompilerRuntimeAbiVersion);
        Assert.True(manifest.SemanticProfile.RuntimeFree);
        Assert.False(manifest.RequiresPowerShellRuntime);
        Assert.False(manifest.UsesPowerShellRuntimeFallback);
        Assert.False(manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(manifest.AllowsPowerShellRuntimeEvaluation);
        Assert.True(manifest.DependencyClosureVerified);
        Assert.Equal(64, manifest.GeneratedSourceSha256.Length);

        var abi = Assert.IsType<PowerShellCompilationAbiManifest>(manifest.PublicAbi);
        Assert.Equal("PowerForge.Compiled", abi.NamespaceName);
        Assert.Equal("ContractProofMethods", abi.TypeName);
        Assert.Equal(64, abi.Sha256.Length);
        var method = Assert.Single(abi.Methods);
        Assert.Equal("Get-ContractProof", method.PowerShellName);
        Assert.Equal("Get_ContractProof", method.ClrName);
        Assert.Equal("Scalar", method.OutputCardinality);
        Assert.Equal("SuccessOutputOnly", method.StreamContract);
        Assert.Equal("ClrDirect", method.ExceptionContract);
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal("Value", parameter.PowerShellName);
        Assert.Equal("Value", parameter.ClrName);
        Assert.True(parameter.Required);
        Assert.False(parameter.Nullable);

        var contractSource = Path.Combine(result.GeneratedSourcePath!, "PowerForgeRuntimeFreeContract.g.cs");
        Assert.True(File.Exists(contractSource));
        Assert.Contains(abi.Sha256, File.ReadAllText(contractSource), StringComparison.Ordinal);

        using var assemblyStream = File.OpenRead(result.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeRuntimeFreeContractProof", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(assemblyStream);
            var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value, StringComparer.Ordinal);
            Assert.Equal(
                PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                metadata["PowerForge.SemanticProfile"]);
            Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeAbiVersion, metadata["PowerForge.CompilerRuntimeAbi"]);
            Assert.Equal(abi.Sha256, metadata["PowerForge.PublicAbiSha256"]);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void Build_StrictLibraryCanBeCalledFromCleanCSharpConsumer()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ContractProof { param([int] $Value) return $Value }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "ContractProofConsumer",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var consumer = Path.Combine(fixture.RootPath, "consumer");
        Directory.CreateDirectory(consumer);
        File.WriteAllText(
            Path.Combine(consumer, "Consumer.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include=\"ContractProofConsumer\"><HintPath>" +
            System.Security.SecurityElement.Escape(result.ArtifactPath!) +
            "</HintPath></Reference></ItemGroup></Project>");
        File.WriteAllText(
            Path.Combine(consumer, "Program.cs"),
            "global::System.Console.Write(global::PowerForge.Compiled.ContractProofConsumerMethods.Get_ContractProof(41));");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = consumer,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add("Consumer.csproj");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "Clean C# consumer did not exit within 120 seconds.");
        Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
        Assert.Equal("41", output.Trim());
        Assert.DoesNotContain("System.Management.Automation", output + error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbiBuilderAndClrSymbolMappingAreDeterministic()
    {
        var parameters = new[] { new PowerShellCompilationParameter("class", "System.Int32", false, true) };
        var first = new PowerShellCompiledMethod("Get-Zeta", "Get_Zeta", "System.Int32", parameters, 4);
        var second = new PowerShellCompiledMethod("Get-Alpha", "Get_Alpha", "System.Int32", parameters, 1);

        var ordered = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { first, second });
        var reversed = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { second, first });

        Assert.Equal(ordered.Sha256, reversed.Sha256);
        Assert.Equal(new[] { "Get-Alpha", "Get-Zeta" }, ordered.Methods.Select(static method => method.PowerShellName));
        Assert.Equal("@class", Assert.Single(ordered.Methods[0].Parameters).ClrName);
        Assert.Equal("_9_name", PowerShellClrSymbolMapper.MapIdentifier("9-name"));
    }
}
