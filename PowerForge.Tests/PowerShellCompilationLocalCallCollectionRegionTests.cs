using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_PinnedLocalCollectionFactoryUnlocksSeveralUnchangedWorkflows()
    {
        var sources = new[]
        {
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "Objects", "New-ArrayList.ps1"),
                Hash: "d5a978413c605a46f480c8a77f5276a2261d6d709e0c33dc0d4dcd0980d9400a"),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "Objects", "Get-ObjectPropertiesAdvanced.ps1"),
                Hash: "2e192ce9e8cbd4ad1a2e4894cd323c87cc40f2893c8c3e21fd463e37ce8a7baa"),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "SQL", "New-SqlQuery.ps1"),
                Hash: "88d133a46e966f7f12f5a1bff0420f9340daee9d75a46cb4f6f37f6a4cc73f28"),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "SQL", "New-SqlQueryAlterTable.ps1"),
                Hash: "bc4430d4076cb11316569b07c9f128857b1e6e8d3fb68561fbc7480dca2e6014"),
            (Path: FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "SQL", "New-SqlQueryCreateTable.ps1"),
                Hash: "1ec7c66c97aee0b86a633ac0903af9826069757108c9577073e751135a46b0a7")
        };
        Assert.All(sources, source => Assert.Equal(
            source.Hash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Path))).ToLowerInvariant()));

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            sources.Select(static source => source.Path),
            "PowerForge.Compiled",
            "PinnedLocalCollectionFactoryMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var expectedCompiledConsumers = new[]
        {
            "New-SqlQuery",
            "New-SqlQueryAlterTable",
            "New-SqlQueryCreateTable"
        };
        Assert.All(expectedCompiledConsumers, consumer =>
        {
            Assert.Contains(typed.Methods, method =>
                method.SourceName.Equals(consumer, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(typed.PromotedRegions, region =>
                region.SourceName.Equals(consumer, StringComparison.OrdinalIgnoreCase));
        });

        var retainedConsumerRegions = typed.PromotedRegions.Where(region =>
            region.SourceName.Equals("Get-ObjectPropertiesAdvanced", StringComparison.OrdinalIgnoreCase) &&
            region.LocalCalls.Any(static call => call.SourceName == "New-ArrayList")).ToArray();
        Assert.NotEmpty(retainedConsumerRegions);
        Assert.All(retainedConsumerRegions, static region =>
        {
            var call = Assert.Single(region.LocalCalls);
            Assert.Equal(PowerShellRegionTransferOwnership.CompiledCalleeFresh, call.ResultContract.Ownership);
            Assert.Equal(PowerShellRegionTransferOutputBehavior.NoEnumerate, call.ResultContract.OutputBehavior);
            Assert.Equal(PowerShellRegionEnumerationOwner.None, call.ResultContract.EnumerationOwner);
            Assert.Equal(PowerShellRegionEnumeratorLifetime.None, call.ResultContract.EnumeratorLifetime);
        });
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedLocalCollectionFactoryPromotesCallerPrefix()
    {
        using var fixture = ArtifactFixture.Create("""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-ClosedList {
                [CmdletBinding()]
                param()
                $Items = New-ClosedList
                data CollectionBarrier { }
                & { $null = $Items.Add('retained') }
                return , $Items
            }
            Export-ModuleMember -Function Get-ClosedList
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "ClosedLocalCollectionFactoryMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        var callable = Assert.Single(typed.Methods, static method => method.SourceName == "New-ClosedList");
        Assert.NotEmpty(callable.ReturnType);
        var matchingRegions = typed.PromotedRegions.Where(static region => region.SourceName == "Get-ClosedList").ToArray();
        Assert.True(matchingRegions.Length == 1,
            System.Text.Json.JsonSerializer.Serialize(typed.RegionCandidates) + Environment.NewLine +
            System.Text.Json.JsonSerializer.Serialize(typed.RegionOpportunities) + Environment.NewLine +
            System.Text.Json.JsonSerializer.Serialize(typed.Diagnostics));
        var region = matchingRegions[0];
        var call = Assert.Single(region.LocalCalls);
        Assert.Equal("New-ClosedList", call.SourceName);
        Assert.Empty(call.ParameterTypes);
        Assert.Equal(typeof(System.Collections.ArrayList).FullName, call.LoweredReturnType);
        Assert.Equal(typeof(System.Collections.ArrayList).FullName, call.ProjectedReturnType);
        Assert.Equal(PowerShellRegionTransferShape.ListSequence, call.ResultContract.Shape);
        Assert.Equal(PowerShellRegionTransferElementContract.OpaqueReference, call.ResultContract.ElementContract);
        Assert.Equal(PowerShellRegionTransferOwnership.CompiledCalleeFresh, call.ResultContract.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.NoEnumerate, call.ResultContract.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferMutation.RetainedOnly, call.ResultContract.Mutation);
        Assert.True(call.ResultContract.Supported);
        var transfer = Assert.Single(region.ContinuationLocals);
        Assert.Equal("Items", transfer.Name, ignoreCase: true);
        Assert.Equal(typeof(System.Collections.ArrayList).FullName, transfer.TypeName);
        Assert.Contains(
            "new global::System.Collections.ArrayList()",
            typed.SourceCode,
            StringComparison.Ordinal);

        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
            System.Text.Json.JsonSerializer.Serialize(region));
        Assert.Equal("New-ClosedList", Assert.Single(roundTrip!.LocalCalls).SourceName);
        Assert.Equal(6, Assert.Single(roundTrip.LocalCalls).ResultContract.SchemaVersion);
    }

    [Theory]
    [InlineData("param([int] $Value)", "$List = [System.Collections.ArrayList]::new()", "return , $List")]
    [InlineData("param()", "$List = [System.Collections.ArrayList]::new(); $null = $List.Count", "return , $List")]
    [InlineData("param()", "$List = [System.Collections.ArrayList]::new(); New-UnprovedList", "return , $List")]
    [InlineData("param()", "$List = [System.Collections.ArrayList]::new()", "return $List")]
    [InlineData("param()", "$List = [System.Collections.ArrayList]::new()", "return , ([System.Collections.ArrayList]::new())")]
    [InlineData("param()", "$null = [System.Collections.ArrayList]::new()", "return , $null")]
    [InlineData("param()", "$true = [System.Collections.ArrayList]::new()", "return , $true")]
    [InlineData("param()", "$Error = [System.Collections.ArrayList]::new()", "return , $Error")]
    [InlineData("param()", "$PSItem = [System.Collections.ArrayList]::new()", "return , $PSItem")]
    [InlineData("param()", "[string] $List = [System.Collections.ArrayList]::new()", "return , $List")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_UnprovedLocalCollectionFactoriesRemainRetained(
        string parameters,
        string initialization,
        string returned)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-UnprovedList {
                [CmdletBinding()]
                {{parameters}}
                {{initialization}}
                {{returned}}
            }
            function Get-UnprovedList {
                [CmdletBinding()]
                param()
                $Items = New-UnprovedList
                data CollectionBarrier { }
                & { $null = $Items.Count }
                return , $Items
            }
            Export-ModuleMember -Function Get-UnprovedList
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "UnprovedLocalCollectionFactoryMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions, static region =>
            region.SourceName == "Get-UnprovedList" && region.LocalCalls.Count > 0);
        Assert.DoesNotContain(typed.RegionCandidates, static candidate =>
            candidate.SourceName == "Get-UnprovedList" && candidate.LocalCalls.Count > 0 && candidate.Promoted);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ClosedLocalCollectionFactoryClosesNativeConsumers(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-ClosedList {
                [CmdletBinding()]
                param([string] $Value = 'seed')
                $Items = New-ClosedList
                [void] $Items.Add($Value)
                return , $Items
            }
            Export-ModuleMember -Function Get-ClosedList
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.ClosedLocalCollectionFactoryConsumer",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.PromotedTypedRegions);
        var consumer = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries,
            static unit => unit.Name == "Get-ClosedList");
        Assert.True(consumer.EmittedClrMethod);
        Assert.True(consumer.UsesNativeFunctionBinding);
        Assert.False(consumer.RetainedHostedSource);
        Assert.Empty(consumer.DiagnosticChain);

        const string probe = """
            $faults = @()
            $firstRecords = @(Get-ClosedList -Value 'alpha' -ErrorVariable +faults)
            $first = $firstRecords[0]
            $second = Get-ClosedList -Value 'beta' -ErrorVariable +faults
            [void] $first.Add('caller-mutation')
            [pscustomobject]@{
                phase = 'shape'
                records = $firstRecords.Count
                type = $first.GetType().FullName
                firstValues = @($first)
                secondValues = @($second)
                fresh = -not [object]::ReferenceEquals($first, $second)
                errors = $faults.Count
            } | ConvertTo-Json -Depth 6 -Compress
            $stopped = @(1..5 | ForEach-Object { Get-ClosedList -Value ([string] $_) } | Select-Object -First 1)
            $reused = Get-ClosedList -Value 'reused'
            [pscustomobject]@{ phase = 'stop-reuse'; stopped = $stopped.Count; reused = @($reused) } |
                ConvertTo-Json -Depth 6 -Compress
            Remove-Module $module -Force
            $module = Import-Module $modulePath -PassThru -Force
            $reimported = Get-ClosedList -Value 'reimported'
            [pscustomobject]@{ phase = 'reimport'; type = $reimported.GetType().FullName; values = @($reimported) } |
                ConvertTo-Json -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-local-collection-factory-consumer-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-local-collection-factory-consumer-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"records\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"fresh\":true", original.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"errors\":0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [InlineData("$Error")]
    [InlineData("$PSItem")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedLocalCollectionFactoryDoesNotCloseNativeConsumersWithRuntimeOwnedAssignments(string target)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-UnprovedList {
                [CmdletBinding()]
                param()
                {{target}} = New-ClosedList
                return 1
            }
            Export-ModuleMember -Function Get-UnprovedList
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "RuntimeOwnedCollectionFactoryAssignmentMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Get-UnprovedList");
    }

    [Theory]
    [InlineData("$Items = [System.Collections.ArrayList]::new(); $Items += New-ClosedList; return $Holder.$Name")]
    [InlineData("$Items = @(New-ClosedList); return $Holder.$Name")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_ClosedLocalCollectionFactoryDoesNotCloseNonExactNativeAssignments(string body)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-UnprovedList {
                [CmdletBinding()]
                param([object] $Holder, [string] $Name = 'Items')
                {{body}}
            }
            Export-ModuleMember -Function Get-UnprovedList
            """, ".psm1");

        var compilation = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) },
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(compilation.Analyzed.Functions,
            static function => function.Symbol.Name == "Get-UnprovedList");
        Assert.DoesNotContain(compilation.Emitted.Methods,
            static method => method.GeneratedName == "Get_UnprovedList");
    }

    [Theory]
    [InlineData("New-ClosedList; return $Holder.$Name")]
    [InlineData("$null = New-ClosedList; return $Holder.$Name")]
    [InlineData("$Holder.$Name = New-ClosedList; return 1")]
    [InlineData("$Holder[$Name] = New-ClosedList; return 1")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_NonAssignmentFactoryCallsStayOnNativeCommandPath(string body)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-NativeList {
                [CmdletBinding()]
                param([object] $Holder, [string] $Name = 'Items')
                {{body}}
            }
            Export-ModuleMember -Function Get-NativeList
            """, ".psm1");

        var compilation = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) },
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var consumer = Assert.Single(compilation.Analyzed.Functions,
            static function => function.Symbol.Name == "Get-NativeList");
        var statements = PowerShellSemanticAnalyzer.EnumerateStatements(consumer.Body).ToArray();
        var expressions = statements
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .ToArray();

        Assert.Equal(PowerShellExecutionDispositionKind.Typed, consumer.Disposition.Kind);
        Assert.True(
            statements.Any(static statement => statement is PowerShellBoundCommandRegionStatement) ||
            expressions.Any(static expression => expression is PowerShellBoundNativeCommandExpression));
        Assert.DoesNotContain(expressions, static expression => expression is PowerShellBoundInvocationExpression
            { ResultProjection: PowerShellLocalCallResultProjection.ClosedCollectionFactory });
        Assert.Contains(compilation.Emitted.Methods,
            static method => method.GeneratedName == "Get_NativeList" && method.NativeFunctionBinding is not null);
    }

    [Theory]
    [InlineData("& New-ClosedList")]
    [InlineData("PSSharedGoods\\New-ClosedList")]
    [InlineData("Missing-List")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_DynamicQualifiedAndUnresolvedCallsDoNotAcquireFactoryContract(string invocation)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-UnprovedList {
                [CmdletBinding()]
                param()
                $Items = {{invocation}}
                data CollectionBarrier { }
                & { $null = $Items.Count }
                return , $Items
            }
            Export-ModuleMember -Function Get-UnprovedList
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "UnprovedLocalCollectionFactoryCalls",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions, static region =>
            region.SourceName == "Get-UnprovedList" && region.LocalCalls.Count > 0);
    }

    [Theory]
    [InlineData("New-ClosedList")]
    [InlineData("return New-ClosedList")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_TerminalFactoryCallsRemainRetainedUntilOutputTransferIsClosed(string terminalCall)
    {
        using var fixture = ArtifactFixture.Create($$"""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-UnprovedList {
                [CmdletBinding()]
                param()
                data CollectionBarrier { }
                {{terminalCall}}
            }
            Export-ModuleMember -Function Get-UnprovedList
            """, ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "TerminalLocalCollectionFactoryCalls",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.PromotedRegions, static region =>
            region.SourceName == "Get-UnprovedList" && region.LocalCalls.Count > 0);
        Assert.DoesNotContain(typed.RegionCandidates, static candidate =>
            candidate.SourceName == "Get-UnprovedList" && candidate.LocalCalls.Count > 0 && candidate.Promoted);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_ClosedLocalCollectionFactoryPreservesFreshAtomicLists(
        string framework,
        string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function New-ClosedList {
                [CmdletBinding()]
                param()
                $List = [System.Collections.ArrayList]::new()
                return , $List
            }
            function Get-ClosedList {
                [CmdletBinding()]
                param()
                $Items = New-ClosedList
                data CollectionBarrier { }
                & {
                    [void] $Items.Add('alpha')
                    [void] $Items.Add($null)
                    [void] $Items.Add('omega')
                }
                return , $Items
            }
            Export-ModuleMember -Function Get-ClosedList
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.ClosedLocalCollectionFactory",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.PromotedTypedRegions);

        const string probe = """
            $faults = @()
            $firstRecords = @(Get-ClosedList -ErrorVariable +faults)
            $first = $firstRecords[0]
            $second = Get-ClosedList -ErrorVariable +faults
            [void] $first.Add('caller-mutation')
            [pscustomobject]@{
                phase = 'shape'
                records = $firstRecords.Count
                type = $first.GetType().FullName
                firstCount = $first.Count
                secondCount = $second.Count
                values = @($first | ForEach-Object { if ($null -eq $_) { '<null>' } else { [string] $_ } })
                fresh = -not [object]::ReferenceEquals($first, $second)
                errors = $faults.Count
            } | ConvertTo-Json -Depth 6 -Compress
            $stopped = @(1..5 | ForEach-Object { Get-ClosedList } | Select-Object -First 1)
            $reused = Get-ClosedList
            [pscustomobject]@{ phase = 'stop-reuse'; stopped = $stopped.Count; reused = $reused.Count } |
                ConvertTo-Json -Depth 6 -Compress
            Remove-Module $module -Force
            $module = Import-Module $modulePath -PassThru -Force
            $reimported = Get-ClosedList
            [pscustomobject]@{ phase = 'reimport'; type = $reimported.GetType().FullName; count = $reimported.Count } |
                ConvertTo-Json -Depth 6 -Compress
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-local-collection-factory-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; $module=Import-Module $modulePath -PassThru -Force; " + probe,
            fixture.RootPath,
            "closed-local-collection-factory-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"records\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"fresh\":true", original.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"errors\":0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
