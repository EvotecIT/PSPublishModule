using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCommandSemanticRegistryTests
{
    [Theory]
    [InlineData("Select-Object", "powerforge.command.projection.select-object")]
    [InlineData("select", "powerforge.command.projection.select-object")]
    [InlineData("Microsoft.PowerShell.Utility\\Select-Object", "powerforge.command.projection.select-object")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Verbose", "powerforge.command.stream.verbose")]
    public void DefaultRegistryResolvesCanonicalAliasesAndModuleQualification(string commandName, string providerId)
    {
        var result = PowerShellCommandSemanticRegistry.Default.Resolve(commandName);

        Assert.Equal(PowerShellCommandResolutionStatus.Resolved, result.Status);
        Assert.Equal(providerId, result.Contract!.ProviderId);
        Assert.True(result.Contract.CompileTimeOnly);
        Assert.False(result.Contract.MayImportSourceModules);
        Assert.False(result.Contract.MayExecuteSource);
    }

    [Fact]
    public void RegistryIsStableAcrossRegistrationOrder()
    {
        var first = Contract("provider.zulu", "Get-Zulu", "Zulu.Module");
        var second = Contract("provider.alpha", "Get-Alpha", "Alpha.Module");

        var forward = new PowerShellCommandSemanticRegistry(new[] { first, second });
        var reverse = new PowerShellCommandSemanticRegistry(new[] { second, first });

        Assert.Equal(forward.Contracts.Select(static contract => contract.ProviderId), reverse.Contracts.Select(static contract => contract.ProviderId));
        Assert.Equal("provider.alpha", forward.Resolve("Get-Alpha").Contract!.ProviderId);
        Assert.Equal("provider.zulu", reverse.Resolve("Zulu.Module\\Get-Zulu").Contract!.ProviderId);
    }

    [Fact]
    public void DuplicateAndUnsafeProvidersFailRegistration()
    {
        var duplicate = Contract("provider.same", "Get-One", "One.Module");
        var duplicateId = Contract("provider.same", "Get-Two", "Two.Module");
        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCommandSemanticRegistry(new[] { duplicate, duplicateId }));
        Assert.Contains("registered more than once", duplicateError.Message, StringComparison.Ordinal);

        var unsafeProvider = Contract("provider.unsafe", "Get-Unsafe", "Unsafe.Module");
        unsafeProvider.MayExecuteSource = true;
        var unsafeError = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCommandSemanticRegistry(new[] { unsafeProvider }));
        Assert.Contains("compile-time-only", unsafeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeProviderRejectsMismatchedOperationProfileAndUnlockedDependencies()
    {
        var provider = RuntimeFreeStreamContract();
        provider.Adapter.Operation = "WriteError";
        var operationError = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCommandSemanticRegistry(new[] { provider }));
        Assert.Contains("does not match stream", operationError.Message, StringComparison.Ordinal);

        provider = RuntimeFreeStreamContract();
        provider.Adapter.SemanticProfile = "PowerForge.RuntimeFree.Strict/999";
        var profileError = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCommandSemanticRegistry(new[] { provider }));
        Assert.Contains("targets semantic profile", profileError.Message, StringComparison.Ordinal);

        provider = RuntimeFreeStreamContract();
        provider.Adapter.Dependencies = new[] { "Unreviewed.Adapter.Dependency" };
        var dependencyError = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCommandSemanticRegistry(new[] { provider }));
        Assert.Contains("cannot yet be locked and certified", dependencyError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnqualifiedConflictsAreAmbiguousButQualifiedCommandsRemainDeterministic()
    {
        var first = Contract("provider.one", "Get-Thing", "One.Module");
        var second = Contract("provider.two", "Get-Thing", "Two.Module");
        var registry = new PowerShellCommandSemanticRegistry(new[] { second, first });

        var ambiguous = registry.Resolve("Get-Thing");

        Assert.Equal(PowerShellCommandResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(new[] { "provider.one", "provider.two" }, ambiguous.Candidates.Select(static contract => contract.ProviderId));
        Assert.Equal("provider.one", registry.Resolve("One.Module\\Get-Thing").Contract!.ProviderId);
        Assert.Equal("provider.two", registry.Resolve("Two.Module\\Get-Thing").Contract!.ProviderId);
    }

    [Fact]
    public void HostedPipelineCarriesProviderContractsAndExplicitPipelineSymbolsThroughLowering()
    {
        var source = "function Get-Pipeline { param([object[]] $InputObject) " +
                     "$InputObject | Where-Object { $_ -ne $null } | ForEach-Object { $PSItem } | Select-Object -First 1 | Sort-Object }";
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "pipeline.ps1");
        var document = PowerShellSourceParser.Parse(source, path);

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var region = Assert.IsType<PowerShellBoundCommandRegionStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        Assert.Equal(
            new[]
            {
                PowerShellCompilationCommandFamily.Filtering,
                PowerShellCompilationCommandFamily.Mapping,
                PowerShellCompilationCommandFamily.Projection,
                PowerShellCompilationCommandFamily.Sorting
            },
            region.Stages.Select(static stage => stage.Provider.Family));
        Assert.Contains(region.Stages.SelectMany(static stage => stage.PipelineSymbols), static symbol => symbol.Symbol.Name == "_");
        Assert.Contains(region.Stages.SelectMany(static stage => stage.PipelineSymbols), static symbol => symbol.Symbol.Name.Equals("PSItem", StringComparison.OrdinalIgnoreCase));
        Assert.All(region.Stages, static stage => Assert.False(stage.Provider.Adapter.RuntimeFree));
        Assert.Collection(
            region.Stages,
            static stage => Assert.IsType<PowerShellBoundFilteringCommandStage>(stage),
            static stage => Assert.IsType<PowerShellBoundMappingCommandStage>(stage),
            static stage => Assert.IsType<PowerShellBoundProjectionCommandStage>(stage),
            static stage => Assert.IsType<PowerShellBoundSortingCommandStage>(stage));

        var lowered = Assert.IsType<PowerShellLoweredCommandRegionStatement>(Assert.Single(Assert.Single(result.Lowered.Functions).Statements));
        Assert.Equal(region.Stages.Select(static stage => stage.Provider.ProviderId), lowered.Stages.Select(static stage => stage.Provider.ProviderId));
        Assert.All(lowered.Stages.SelectMany(static stage => stage.PipelineSymbols), static symbol => Assert.Equal(PowerShellSymbolKind.PipelineVariable, symbol.Kind));
        Assert.Collection(
            lowered.Stages,
            static stage => Assert.IsType<PowerShellLoweredFilteringCommandStage>(stage),
            static stage => Assert.IsType<PowerShellLoweredMappingCommandStage>(stage),
            static stage => Assert.IsType<PowerShellLoweredProjectionCommandStage>(stage),
            static stage => Assert.IsType<PowerShellLoweredSortingCommandStage>(stage));
    }

    [Fact]
    public void ModuleQualifiedStreamCommandUsesTheRegisteredRuntimeFreeBinder()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Proof { Microsoft.PowerShell.Utility\\Write-Verbose 'proof' }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "stream.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var stream = Assert.IsType<PowerShellBoundStreamWriteStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        Assert.Equal(PowerShellStreamCommandKind.Verbose, stream.Kind);
        Assert.Contains("__writeVerbose", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
        var provider = PowerShellCommandSemanticRegistry.Default.Resolve("Microsoft.PowerShell.Utility\\Write-Verbose").Contract!;
        Assert.True(provider.Adapter.RuntimeFree);
        Assert.True(provider.Adapter.AotCompatible);
        Assert.Empty(provider.Adapter.Dependencies);
    }

    [Fact]
    public void RuntimeFreeStreamFamiliesLowerToCompleteClrSinkContracts()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-AllStreams { Write-Output 42; Write-Verbose 'verbose'; Write-Debug 'debug'; Write-Warning 'warning'; Write-Information 'information'; Write-Host 'host'; Write-Error 'error' }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "streams.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var streams = Assert.Single(result.Analyzed.Functions).Body.Statements.Cast<PowerShellBoundStreamWriteStatement>().ToArray();
        Assert.Equal(
            new[] { PowerShellStreamCommandKind.Success, PowerShellStreamCommandKind.Verbose, PowerShellStreamCommandKind.Debug, PowerShellStreamCommandKind.Warning, PowerShellStreamCommandKind.Information, PowerShellStreamCommandKind.Host, PowerShellStreamCommandKind.Error },
            streams.Select(static stream => stream.Kind));
        var source = Assert.Single(result.Emitted.Methods).Source;
        foreach (var sink in new[] { "__writeOutput", "__writeVerbose", "__writeDebug", "__writeWarning", "__writeInformation", "__writeHost", "__writeError" })
            Assert.Contains(sink, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Write-Output -InputObject 42", "InputObject")]
    [InlineData("Write-Verbose -Message 'verbose'", "Message")]
    [InlineData("Write-Debug -Message 'debug'", "Message")]
    [InlineData("Write-Warning -Message 'warning'", "Message")]
    [InlineData("Write-Information -MessageData 'information'", "MessageData")]
    [InlineData("Write-Host -Object 'host'", "Object")]
    [InlineData("Write-Error -Message 'error'", "Message")]
    public void RuntimeFreeStreamProvidersOwnTheirNamedParameterShape(string invocation, string expectedParameter)
    {
        var commandName = invocation.Substring(0, invocation.IndexOf(' '));
        var provider = PowerShellCommandSemanticRegistry.Default.Resolve(commandName).Contract!;
        var parameter = Assert.Single(provider.Parameters);
        Assert.Equal(expectedParameter, parameter.Name);

        var document = PowerShellSourceParser.Parse(
            $"function Write-Proof {{ {invocation} }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", commandName + ".ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.IsType<PowerShellBoundStreamWriteStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
    }

    [Fact]
    public void RuntimeFreeStreamProviderRejectsAnotherCommandsNamedParameterShape()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Proof { Write-Output -Message 42 }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "wrong-parameter.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.IsNotType<PowerShellBoundStreamWriteStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
    }

    private static PowerShellCompilationCommandProviderContract Contract(string providerId, string commandName, string moduleName)
        => new()
        {
            ProviderId = providerId,
            ProviderVersion = "1.0",
            FeatureId = PowerShellCompilationFeatureIds.ForCommand(commandName),
            Family = PowerShellCompilationCommandFamily.HostedRegion,
            CommandName = commandName,
            ModuleNames = new[] { moduleName },
            Output = PowerShellCompilationCommandOutput.Unknown,
            Cardinality = PowerShellCompilationCommandCardinality.Unknown,
            Stream = "Success+PowerShell",
            Errors = PowerShellCompilationCommandErrors.PowerShellHost,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                SemanticProfile = "PowerShell.Hosted/1.0",
                Dependencies = new[] { "System.Management.Automation" }
            }
        };

    private static PowerShellCompilationCommandProviderContract RuntimeFreeStreamContract()
        => new()
        {
            ProviderId = "tests.runtime-free.stream",
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
}
