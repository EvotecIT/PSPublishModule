using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCommandSemanticRegistryTests
{
    [Fact]
    public void CommandFamilyOrdinalsRemainBackwardCompatible()
    {
        Assert.Equal(7, (int)PowerShellCompilationCommandFamily.ExternalOperation);
        Assert.Equal(8, (int)PowerShellCompilationCommandFamily.CommandDiscovery);
        Assert.Equal(9, (int)PowerShellCompilationCommandFamily.ClrConstruction);
        Assert.Equal(10, (int)PowerShellCompilationCommandFamily.HostedBooleanQuery);
        Assert.Equal(11, (int)PowerShellCompilationCommandFamily.RuntimeState);
    }

    [Theory]
    [InlineData("Select-Object", "powerforge.command.projection.select-object")]
    [InlineData("select", "powerforge.command.projection.select-object")]
    [InlineData("Microsoft.PowerShell.Utility\\Select-Object", "powerforge.command.projection.select-object")]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Verbose", "powerforge.command.stream.verbose")]
    [InlineData("Get-Command", "powerforge.command.discovery.get-command")]
    [InlineData("gcm", "powerforge.command.discovery.get-command")]
    [InlineData("Microsoft.PowerShell.Core\\Get-Command", "powerforge.command.discovery.get-command")]
    [InlineData("Test-Path", "powerforge.command.hosted-boolean.test-path")]
    [InlineData("Microsoft.PowerShell.Management\\Test-Path", "powerforge.command.hosted-boolean.test-path")]
    [InlineData("Get-Date", "powerforge.command.runtime-state.get-date")]
    [InlineData("Microsoft.PowerShell.Utility\\Get-Date", "powerforge.command.runtime-state.get-date")]
    [InlineData("New-Object", "powerforge.command.construction.new-object")]
    [InlineData("Microsoft.PowerShell.Utility\\New-Object", "powerforge.command.construction.new-object")]
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
    public void HostedTestPathContractUsesOnlyCrossProfileAliases()
    {
        var provider = PowerShellCommandSemanticRegistry.Default.Resolve("Test-Path").Contract!;
        var literalPath = Assert.Single(provider.Parameters, static parameter => parameter.Name == "LiteralPath");
        var errorAction = Assert.Single(provider.Parameters, static parameter => parameter.Name == "ErrorAction");

        Assert.Equal(new[] { "PSPath" }, literalPath.Aliases);
        Assert.Equal(new[] { "EA" }, errorAction.Aliases);
        Assert.DoesNotContain(provider.Parameters, static parameter => parameter.Name == "Path");
        Assert.Equal(PowerShellCompilationCommandErrors.None, provider.Errors);
    }

    [Fact]
    public void RuntimeStateGetDateContractIsRuntimeFreeAndArgumentless()
    {
        var provider = PowerShellCommandSemanticRegistry.Default.Resolve("Get-Date").Contract!;

        Assert.Equal(PowerShellCompilationCommandFamily.RuntimeState, provider.Family);
        Assert.Empty(provider.Parameters);
        Assert.Empty(provider.Aliases);
        Assert.Equal(PowerShellCompilationCommandOutput.Projected, provider.Output);
        Assert.Equal(PowerShellCompilationCommandCardinality.Scalar, provider.Cardinality);
        Assert.Equal(PowerShellCompilationCommandErrors.None, provider.Errors);
        Assert.Equal("ReadCurrentLocalDateTime", provider.Adapter.Operation);
        Assert.True(provider.Adapter.RuntimeFree);
        Assert.True(provider.Adapter.AotCompatible);
        Assert.Empty(provider.Adapter.Dependencies);
        Assert.Null(provider.Adapter.EntryPoint);
    }

    [Fact]
    public void NestedStreamMessagesExposeHostedProvidersToGenericWalkers()
    {
        var span = new SourceSpan("nested-stream", 0, 1, 1, 1, 1, 2);
        var hostedProvider = PowerShellCommandSemanticRegistry.Default.Resolve("Test-Path").Contract!;
        var streamProvider = PowerShellCommandSemanticRegistry.Default.Resolve("Write-Output").Contract!;
        var stringType = new PowerShellTypeFact(
            typeof(string),
            PowerShellTypeFactProvenance.Literal,
            "Synthetic traversal contract.");
        var boundHosted = new PowerShellBoundHostedBooleanCommandExpression(
            span,
            hostedProvider,
            new[]
            {
                new PowerShellBoundHostedCommandArgument("LiteralPath", new PowerShellBoundLiteralExpression(span, "FileSystem::proof", stringType, PowerShellValueState.Known)),
                new PowerShellBoundHostedCommandArgument("ErrorAction", new PowerShellBoundLiteralExpression(span, "Ignore", stringType, PowerShellValueState.Known))
            });
        var boundStream = new PowerShellBoundStreamWriteStatement(span, PowerShellStreamCommandKind.Success, streamProvider, boundHosted);

        Assert.Same(boundHosted, Assert.Single(PowerShellSemanticAnalyzer.EnumerateDirectExpressions(boundStream)));
        Assert.Contains(PowerShellSemanticAnalyzer.EnumerateExpressions(boundHosted), static expression => expression is PowerShellBoundHostedBooleanCommandExpression);

        var loweredHosted = new PowerShellLoweredHostedBooleanCommandExpression(
            span,
            hostedProvider,
            new[]
            {
                new PowerShellLoweredHostedCommandArgument("LiteralPath", new PowerShellLoweredLiteralExpression(span, typeof(string), "FileSystem::proof")),
                new PowerShellLoweredHostedCommandArgument("ErrorAction", new PowerShellLoweredLiteralExpression(span, typeof(string), "Ignore"))
            });
        var loweredStream = new PowerShellLoweredStreamWriteStatement(span, PowerShellStreamCommandKind.Success, streamProvider, loweredHosted);

        Assert.Equal(
            new[] { "powerforge.command.hosted-boolean.test-path", "powerforge.command.stream.output" },
            PowerShellLoweredCommandProviderCollector.Collect(new[] { loweredStream }).Select(static provider => provider.ProviderId));
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

    [Theory]
    [InlineData(PowerShellCompilationCommandFamily.ClrConstruction)]
    [InlineData(PowerShellCompilationCommandFamily.RuntimeState)]
    public void ExtensionsCannotRegisterCompilerOwnedFamilies(PowerShellCompilationCommandFamily family)
    {
        var extension = Contract("provider.compiler-owned", "Get-CompilerOwned", "Compiler.Owned");
        extension.Family = family;

        var error = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCommandSemanticRegistry.Create(new[] { extension }));

        Assert.Contains("cannot extend compiler-owned command family", error.Message, StringComparison.Ordinal);
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
        Assert.Contains("built-in provider", dependencyError.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("stream")]
    [InlineData("output")]
    [InlineData("cardinality")]
    [InlineData("errors")]
    [InlineData("aot")]
    [InlineData("runtime-free")]
    [InlineData("cancellation")]
    [InlineData("timeout")]
    [InlineData("cleanup")]
    [InlineData("dependencies")]
    [InlineData("entrypoint")]
    [InlineData("parameters")]
    public void RuntimeStateProviderRejectsEveryMalformedIntrinsicContract(string mutation)
    {
        var provider = RuntimeStateContract();
        switch (mutation)
        {
            case "operation": provider.Adapter.Operation = "ReadAnotherValue"; break;
            case "stream": provider.Stream = "Information"; break;
            case "output": provider.Output = PowerShellCompilationCommandOutput.Unknown; break;
            case "cardinality": provider.Cardinality = PowerShellCompilationCommandCardinality.Collection; break;
            case "errors": provider.Errors = PowerShellCompilationCommandErrors.Terminating; break;
            case "aot": provider.Adapter.AotCompatible = false; break;
            case "runtime-free": provider.Adapter.RuntimeFree = false; break;
            case "cancellation": provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative; break;
            case "timeout": provider.Adapter.ProcessIsolationTimeoutSeconds = 1; break;
            case "cleanup": provider.Adapter.Cleanup = PowerShellCompilationProviderCleanup.Deterministic; break;
            case "dependencies": provider.Adapter.Dependencies = new[] { "Another.Dependency" }; break;
            case "entrypoint": provider.Adapter.EntryPoint = new PowerShellCompilationProviderAdapterEntryPoint(); break;
            case "parameters": provider.Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "Value", Position = 0 } }; break;
            default: throw new InvalidOperationException($"Unknown mutation '{mutation}'.");
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationProviderContractValidator.ValidateExecutableContractShape(provider, requireExecutableEntryPoint: false));

        Assert.Contains("invalid intrinsic contract", error.Message, StringComparison.Ordinal);
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
                     "$InputObject | Microsoft.PowerShell.Core\\Where-Object { $_ -ne $null } | Microsoft.PowerShell.Core\\ForEach-Object { $PSItem } | Microsoft.PowerShell.Utility\\Select-Object -First 1 | Microsoft.PowerShell.Utility\\Sort-Object }";
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
            "function Write-AllStreams { Microsoft.PowerShell.Utility\\Write-Output 42; Microsoft.PowerShell.Utility\\Write-Verbose 'verbose'; Microsoft.PowerShell.Utility\\Write-Debug 'debug'; Microsoft.PowerShell.Utility\\Write-Warning 'warning'; Microsoft.PowerShell.Utility\\Write-Information 'information'; Microsoft.PowerShell.Utility\\Write-Host 'host'; Microsoft.PowerShell.Utility\\Write-Error 'error' }",
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

    [Fact]
    public void RuntimeFreeStreamProviderAcceptsProvablyNonEmptyExpandedMessage()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Proof { param([int] $Value) Microsoft.PowerShell.Utility\\Write-Verbose \"Current value: $Value\" }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "expanded-stream.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.IsType<PowerShellBoundStreamWriteStatement>(Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements));
        Assert.Contains("Current value:", Assert.Single(result.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeStreamProviderRejectsExpandedMessageWithoutLiteralContent()
    {
        var document = PowerShellSourceParser.Parse(
            "function Write-Proof { param([string] $Value) Microsoft.PowerShell.Utility\\Write-Verbose \"$Value\" }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "CommandRegistry", "empty-expanded-stream.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Contains(result.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "command.write-verbose");
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
            $"function Write-Proof {{ Microsoft.PowerShell.Utility\\{invocation} }}",
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
            "function Write-Proof { Microsoft.PowerShell.Utility\\Write-Output -Message 42 }",
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

    private static PowerShellCompilationCommandProviderContract RuntimeStateContract()
        => new()
        {
            ProviderId = "tests.runtime-state.current-local-date",
            ProviderVersion = "1.0",
            FeatureId = "tests.command.current-local-date",
            Family = PowerShellCompilationCommandFamily.RuntimeState,
            CommandName = "Get-CurrentLocalDate",
            Output = PowerShellCompilationCommandOutput.Projected,
            Cardinality = PowerShellCompilationCommandCardinality.Scalar,
            Stream = "Success",
            Errors = PowerShellCompilationCommandErrors.None,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "ReadCurrentLocalDateTime",
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = true
            }
        };
}
