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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        Assert.Equal(6, manifest.SchemaVersion);
        Assert.False(manifest.DependencyLockReviewed);
        Assert.NotNull(manifest.SemanticProfile);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeStrictName, manifest.SemanticProfile.Name);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion, manifest.SemanticProfile.Version);
        Assert.Equal(PowerShellCompilationSemanticProfile.RuntimeFreeAbiVersion, manifest.SemanticProfile.CompilerRuntimeAbiVersion);
        Assert.True(manifest.SemanticProfile.RuntimeFree);
        Assert.False(manifest.SemanticProfile.HasRuntimeSubstrate);
        Assert.Equal("None", manifest.SemanticProfile.RuntimeSubstrate);
        Assert.False(manifest.RequiresPowerShellRuntime);
        Assert.False(manifest.UsesPowerShellRuntimeFallback);
        Assert.False(manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(manifest.AllowsPowerShellRuntimeEvaluation);
        Assert.True(manifest.DependencyClosureVerified);
        Assert.NotNull(manifest.DependencyClosure);
        Assert.True(manifest.DependencyClosure.Verified);
        Assert.True(manifest.DependencyClosure.InspectedFiles > 0);
        Assert.True(manifest.DependencyClosure.ManagedAssemblies > 0);
        Assert.Empty(manifest.DependencyClosure.Limitations);
        Assert.Equal(64, manifest.GeneratedSourceSha256.Length);

        var abi = Assert.IsType<PowerShellCompilationAbiManifest>(manifest.PublicAbi);
        Assert.Equal(4, abi.SchemaVersion);
        Assert.Equal("PowerForge.Compiled", abi.NamespaceName);
        Assert.Equal("ContractProofMethods", abi.TypeName);
        Assert.Equal(64, abi.Sha256.Length);
        var method = Assert.Single(abi.Methods);
        Assert.Equal("Get-ContractProof", method.PowerShellName);
        Assert.Equal("Get_ContractProof", method.ClrName);
        Assert.Equal("Scalar", method.OutputCardinality);
        Assert.Equal(new[] { "Unknown" }, method.OutputValueStates);
        Assert.Equal("PreserveScalar", method.OutputScalarization);
        Assert.Empty(method.CollectionElementType);
        Assert.False(method.CanProduceNoOutput);
        Assert.False(method.CanProduceNull);
        Assert.True(method.NoOutputDistinctFromNull);
        Assert.Equal("SuccessOutputOnly", method.StreamContract);
        Assert.Equal("ClrDirect", method.ExceptionContract);
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal("Value", parameter.PowerShellName);
        Assert.Equal("Value", parameter.ClrName);
        Assert.True(parameter.Required);
        Assert.False(parameter.Nullable);
        Assert.Equal(new[] { "v" }, parameter.Aliases);
        Assert.False(parameter.CompilerAdded);
        Assert.True(Assert.Single(parameter.Bindings).Mandatory);

        var contractSource = Path.Combine(result.GeneratedSourcePath!, "PowerForgeRuntimeFreeContract.g.cs");
        Assert.True(File.Exists(contractSource));
        Assert.Contains(abi.Sha256, File.ReadAllText(contractSource), StringComparison.Ordinal);
        var generatedProject = Assert.Single(Directory.EnumerateFiles(result.GeneratedSourcePath!, "*.csproj"));
        var generatedProjectText = File.ReadAllText(generatedProject);
        Assert.Contains("<ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>", generatedProjectText, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors Condition=\"'$(PublishTrimmed)' == 'true' or '$(PublishAot)' == 'true'\">true</TreatWarningsAsErrors>", generatedProjectText, StringComparison.Ordinal);
        Assert.DoesNotContain("IL2026", generatedProjectText, StringComparison.Ordinal);

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
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

    [Fact]
    public void AbiUsesBoundOutputFactsForNoOutputCollectionAndNullStates()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-Proof { [int] $value = 1 }\n" +
            "function Get-ProofItems { return @(1, 2) }\n" +
            "function Get-ProofNull { return [Nullable[int]] $null }");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        var abi = PowerShellCompilationAbiBuilder.Create(typed.NamespaceName, typed.TypeName, typed.Methods);

        var noOutput = Assert.Single(abi.Methods, static method => method.PowerShellName == "Set-Proof");
        Assert.Equal("None", noOutput.OutputCardinality);
        Assert.Equal("NoOutput", noOutput.OutputScalarization);
        Assert.True(noOutput.CanProduceNoOutput);
        Assert.False(noOutput.CanProduceNull);
        Assert.Empty(noOutput.OutputValueStates);

        var collection = Assert.Single(abi.Methods, static method => method.PowerShellName == "Get-ProofItems");
        Assert.Equal("Collection", collection.OutputCardinality);
        Assert.Equal("EnumerateCollection", collection.OutputScalarization);
        Assert.Equal(typeof(int).FullName, collection.CollectionElementType);
        Assert.Equal(new[] { "Known" }, collection.OutputValueStates);

        var nullable = Assert.Single(abi.Methods, static method => method.PowerShellName == "Get-ProofNull");
        Assert.Equal("Scalar", nullable.OutputCardinality);
        Assert.Equal(new[] { "Null" }, nullable.OutputValueStates);
        Assert.True(nullable.CanProduceNull);
        Assert.True(nullable.Nullable);
        Assert.True(nullable.NoOutputDistinctFromNull);
        Assert.False(nullable.CanProduceNoOutput);
    }

    [Fact]
    public void AbiAggregatesSuccessStreamOutputAndTreatsUnknownReferenceValuesAsNullable()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MixedOutput { Write-Output 1; return 2 }\n" +
            "function Get-Passthrough { param([AllowNull()][object] $Value) return $Value }");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        var abi = PowerShellCompilationAbiBuilder.Create(typed.NamespaceName, typed.TypeName, typed.Methods);

        var mixed = Assert.Single(abi.Methods, static method => method.PowerShellName == "Get-MixedOutput");
        Assert.Equal("Collection", mixed.OutputCardinality);
        Assert.Equal("EnumerateCollection", mixed.OutputScalarization);
        Assert.Equal("SuccessAndNonSuccessStreams", mixed.StreamContract);
        Assert.Contains(mixed.Parameters, static parameter => parameter.CompilerPurpose == "SuccessStream");

        var passthrough = Assert.Single(abi.Methods, static method => method.PowerShellName == "Get-Passthrough");
        Assert.Contains("Unknown", passthrough.OutputValueStates);
        Assert.True(passthrough.CanProduceNull);
        Assert.True(passthrough.Nullable);
    }

    [Fact]
    public void AbiHashCoversPowerShellBindingAndCompilerAddedClrParameters()
    {
        var firstContract = new PowerShellCompilationParameter(
            "Mode",
            "System.String",
            hasDefaultValue: true,
            isMandatory: false,
            isSwitch: false,
            aliases: new[] { "m" },
            allowNull: false,
            validations: new[] { new PowerShellCompilationValidation(PowerShellCompilationValidationKind.Set, new[] { "A", "B" }) },
            bindings: new[] { new PowerShellCompilationParameterBinding("ByMode", position: 0, valueFromRemainingArguments: true) },
            defaultValue: new PowerShellCompilationLiteral(PowerShellCompilationLiteralKind.String, "System.String", "A"));
        var secondContract = new PowerShellCompilationParameter(
            "Mode",
            "System.String",
            hasDefaultValue: true,
            isMandatory: false,
            isSwitch: false,
            aliases: new[] { "m" },
            allowNull: false,
            validations: new[] { new PowerShellCompilationValidation(PowerShellCompilationValidationKind.Set, new[] { "A", "B" }) },
            bindings: new[] { new PowerShellCompilationParameterBinding("ByMode", position: 1, valueFromRemainingArguments: true) },
            defaultValue: new PowerShellCompilationLiteral(PowerShellCompilationLiteralKind.String, "System.String", "A"));
        var binding = new PowerShellCompilationCommandBinding(
            isAdvancedFunction: true,
            positionalBinding: false,
            defaultParameterSetName: "ByMode",
            supportsShouldProcess: true,
            confirmImpact: "High");
        var first = new PowerShellCompiledMethod(
            "Invoke-Proof", "Invoke_Proof", "System.String", new[] { firstContract }, 1, null,
            false, false, new[] { "ip" }, true, true, binding, false, "System.String");
        var second = new PowerShellCompiledMethod(
            "Invoke-Proof", "Invoke_Proof", "System.String", new[] { secondContract }, 1, null,
            false, false, new[] { "ip" }, true, true, binding, false, "System.String");

        var firstAbi = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { first });
        var secondAbi = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { second });

        Assert.NotEqual(firstAbi.Sha256, secondAbi.Sha256);
        var method = Assert.Single(firstAbi.Methods);
        Assert.True(method.IsAdvancedFunction);
        Assert.False(method.PositionalBinding);
        Assert.Equal("ByMode", method.DefaultParameterSetName);
        Assert.True(method.SupportsShouldProcess);
        Assert.Equal("High", method.ConfirmImpact);
        Assert.Equal(new[] { "ip" }, method.Aliases);
        Assert.Collection(
            method.Parameters,
            authored =>
            {
                Assert.False(authored.CompilerAdded);
                Assert.True(authored.HasDefaultValue);
                Assert.Equal("A", authored.DefaultValue?.Value);
                Assert.True(Assert.Single(authored.Bindings).ValueFromRemainingArguments);
            },
            generated =>
            {
                Assert.True(generated.CompilerAdded);
                Assert.Equal("__boundParameters", generated.ClrName);
                Assert.Equal("BoundParameterNames", generated.CompilerPurpose);
            });
    }

    [Fact]
    public void AbiHashCoversCommandProviderAndAdapterVersions()
    {
        PowerShellCompilationCommandProviderContract Provider(string version) => new()
        {
            ProviderId = "proof.command.provider",
            ProviderVersion = version,
            FeatureId = "command.proof",
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = "Write-Proof",
            Output = PowerShellCompilationCommandOutput.None,
            Cardinality = PowerShellCompilationCommandCardinality.None,
            Stream = "Verbose",
            Errors = PowerShellCompilationCommandErrors.Terminating,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = true
            }
        };
        PowerShellCompiledMethod Method(PowerShellCompilationCommandProviderContract provider) => new(
            "Write-Proof", "Write_Proof", "System.Void", Array.Empty<PowerShellCompilationParameter>(), 1, null,
            false, false, null, false, false, null, false, string.Empty,
            commandProviders: new[] { provider });

        var first = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { Method(Provider("1.0")) });
        var second = PowerShellCompilationAbiBuilder.Create("Proof", "Commands", new[] { Method(Provider("2.0")) });

        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.Equal("1.0", Assert.Single(Assert.Single(first.Methods).CommandProviders).ProviderVersion);
    }
}
