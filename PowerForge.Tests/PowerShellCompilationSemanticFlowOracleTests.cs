using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    public static TheoryData<string, string, string, string> RuntimeFreeFlowCases => new()
    {
        {
            "function-graph",
            "function Get-BaseValue { return 42 }\nfunction Get-Result { return Get-BaseValue }",
            "Get_Result",
            "42"
        },
        {
            "operator-comparison",
            "function Get-ComparisonValue { return 42 -eq 42 }",
            "Get_ComparisonValue",
            "True"
        },
        {
            "operator-logical",
            "function Get-LogicalValue { return $true -and -not $false }",
            "Get_LogicalValue",
            "True"
        },
        {
            "operator-arithmetic",
            "function Get-ArithmeticValue { [int] $Value = 6; $Value *= 7; return $Value }",
            "Get_ArithmeticValue",
            "42"
        },
        {
            "expandable-string",
            "function Get-ExpandableStringResult { [string] $Value = '42'; return [string]::CompareOrdinal(\"value=$Value\", 'value=42') }",
            "Get_ExpandableStringResult",
            "0"
        },
        {
            "catch-filter",
            "function Get-CatchValue { try { return [int]::Parse('expected') } catch [System.InvalidOperationException] { return -1 } catch [System.FormatException] { return 42 } catch { return -2 } }",
            "Get_CatchValue",
            "42"
        },
        {
            "switch-flags",
            "function Get-SwitchFlagValue { switch -Regex ('forty-two') { '^forty' { return 42 } default { return -1 } } }",
            "Get_SwitchFlagValue",
            "42"
        },
        {
            "parameter-metadata",
            "function Get-ValidatedValue { [CmdletBinding()] param([ValidateRange(1, 100)][int] $Value) return $Value } function Get-MetadataResult { [bool] $Rejected = $false; try { $null = Get-ValidatedValue -Value 101 } catch { $Rejected = $true }; if (-not $Rejected) { return -1 }; [int] $Result = Get-ValidatedValue -Value 40; $Result += 2; return $Result }",
            "Get_MetadataResult",
            "42"
        },
        {
            "pipeline-enumeration",
            "function Get-PipelineEnumerationValue { [int] $Total = 0; 40, 2 | ForEach-Object { $Total += $_ }; return $Total }",
            "Get_PipelineEnumerationValue",
            "42"
        }
    };

    [Theory]
    [MemberData(nameof(RuntimeFreeFlowCases))]
    public void RuntimeFreeFlowCaseExecutesAcrossTargets(
        string caseName,
        string source,
        string methodName,
        string expectedValue)
    {
        foreach (var targetFramework in new[] { "net472", "net8.0", "net10.0" })
        {
            using var fixture = OracleFixture.Create(source);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                Path.Combine(fixture.RootPath, targetFramework),
                "Bounded" + caseName.Replace("-", string.Empty, StringComparison.Ordinal) + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = targetFramework,
                SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                SingleFile = false
            });

            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.False(build.Manifest!.RequiresPowerShellRuntime);
            var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
            var method = assembly.GetTypes()
                .SelectMany(static type => type.GetMethods())
                .Single(candidate => candidate.Name == methodName);
            Assert.Equal(expectedValue, Convert.ToString(method.Invoke(null, null), System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreePipelineParameterEnumerationExecutesAcrossTargets(string targetFramework)
    {
        const string source = "function Get-PipelineTotal { param([int[]] $Values) " +
                              "[int] $Total = 0; $Values | ForEach-Object { $Total += $_ }; return $Total }";
        using var fixture = OracleFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "PipelineParameter" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_PipelineTotal");
        Assert.Equal(42, method.Invoke(null, new object?[] { new[] { 40, 2 } }));
        Assert.Equal(0, method.Invoke(null, new object?[] { null }));
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreePipelineLifecycleExecutesAcrossTargets(string targetFramework)
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/pipeline-lifecycle");
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedPipelineLifecycle" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);
        Assert.Equal("42", Assert.Single(observation.Success).Value);
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeCommentHelpExecutesAcrossTargets(string targetFramework)
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/comment-based-help");
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedCommentHelp" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);
        Assert.Equal("42", Assert.Single(observation.Success).Value);
    }

    [Fact]
    public void RuntimeFreeCommentHelpLookupAvoidsAuthoredTemporaryCollisions()
    {
        const string source = "function Get-Documented {\n<#\n.SYNOPSIS\nDocumented.\n#>\n42\n}\n[string] $__pf_dictionary = 'authored'; [string] $__pf_value = 'authored'; $Help = Get-Help Get-Documented; if ($__pf_dictionary -cne 'authored' -or $__pf_value -cne 'authored' -or $Help.Name -cne 'Get-Documented' -or $Help.Synopsis -cne 'Documented.') { return -1 }; Get-Documented";
        using var fixture = OracleFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "collision"),
            "BoundedCommentHelpCollision",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);
        Assert.Equal("42", Assert.Single(observation.Success).Value);
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesFunctionGraphCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/function-graph", "BoundedFunctionGraphOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesComparisonCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/operator-comparison", "BoundedComparisonOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesLogicalCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/operator-logical", "BoundedLogicalOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesArithmeticCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/operator-arithmetic", "BoundedArithmeticOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesExpandableStringCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/expandable-string", "BoundedExpandableStringOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesCatchFilterCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/catch-filter", "BoundedCatchFilterOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesSwitchFlagsCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/switch-flags", "BoundedSwitchFlagsOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesParameterMetadataCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/parameter-metadata", "BoundedParameterMetadataOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesPipelineEnumerationCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/pipeline-enumeration", "BoundedPipelineEnumerationOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesPipelineLifecycleCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/pipeline-lifecycle", "BoundedPipelineLifecycleOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesCommentHelpCaseAgainstPinnedHost()
        => QualifyRuntimeFreeFlowCase("PowerForge.Semantic/comment-based-help", "BoundedCommentHelpOracle");

    [Fact]
    public void RuntimeFreeArtifactObserverSelectsOnlyTheGeneratedExecutableEntryPoint()
    {
        var entryPoint = new PowerShellCompilationAbiMethod { PowerShellName = "<script>", ClrName = "Invoke" };
        var helper = new PowerShellCompilationAbiMethod { PowerShellName = "Get-BaseValue", ClrName = "Get_BaseValue" };

        Assert.Same(
            entryPoint,
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.SelectEntryPoint(
                CreatePublicAbi(helper, entryPoint)));
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsMissingOrAmbiguousGeneratedEntryPoints()
    {
        var helper = new PowerShellCompilationAbiMethod { PowerShellName = "Get-BaseValue", ClrName = "Get_BaseValue" };
        var first = new PowerShellCompilationAbiMethod { PowerShellName = "<script>", ClrName = "Invoke" };
        var second = new PowerShellCompilationAbiMethod { PowerShellName = "<script>", ClrName = "Invoke" };

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.SelectEntryPoint(
                CreatePublicAbi(helper)));
        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.SelectEntryPoint(
                CreatePublicAbi(first, second)));
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsStaleHashAfterEntryPointOrOutputForgery()
    {
        var helper = new PowerShellCompilationAbiMethod { PowerShellName = "Get-BaseValue", ClrName = "Get_BaseValue" };
        var relabeled = CreatePublicAbi(helper);
        helper.PowerShellName = "<script>";
        helper.ClrName = "Invoke";

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.SelectEntryPoint(relabeled));

        var entryPoint = new PowerShellCompilationAbiMethod
        {
            PowerShellName = "<script>",
            ClrName = "Invoke",
            ReturnType = "System.Int32",
            OutputCardinality = "Scalar",
            OutputValueStates = new[] { "Known" }
        };
        var alteredOutput = CreatePublicAbi(entryPoint);
        entryPoint.ReturnType = "System.String";

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.SelectEntryPoint(alteredOutput));
    }

    [Fact]
    public void RuntimeFreeArtifactObserverBindsPublicAbiHashToGeneratedAssemblyMetadata()
    {
        using var fixture = OracleFixture.Create("42");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict-metadata"),
            "BoundedAbiMetadataOracle",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(build.Manifest);
        var publicAbi = Assert.IsType<PowerShellCompilationAbiManifest>(manifest.PublicAbi);
        PowerShellCompilationSemanticRuntimeFreeArtifactObserver.ValidateEmbeddedPublicAbi(manifest, publicAbi.Sha256);
        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeArtifactObserver.ValidateEmbeddedPublicAbi(manifest, new string('0', 64)));
    }

    private static void QualifyRuntimeFreeFlowCase(string caseId, string artifactName)
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get(caseId);
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
            {
                HostExecutablePath = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"),
                ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
            });
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            artifactName,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = pin.ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(pin.ProfileId, build);
        var allowed = new[] { "Encoding", "ExitCode" };
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, allowed));
        var differences = PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            semanticCase.FeatureId,
            new[] { interpreted, strict },
            allowed,
            "The interpreted script has no enclosing process exit contract and host encoding differs from the Strict UTF-8 executable contract.");
        Assert.Equal(
            new[] { "Encoding", "ExitCode" },
            differences.Select(static difference => difference.Path).OrderBy(static path => path, StringComparer.Ordinal));
    }

    private static PowerShellCompilationAbiManifest CreatePublicAbi(params PowerShellCompilationAbiMethod[] methods)
    {
        var abi = new PowerShellCompilationAbiManifest
        {
            NamespaceName = "PowerForge.Compiled",
            TypeName = "GeneratedMethods",
            Methods = methods
        };
        abi.Sha256 = PowerShellCompilationAbiBuilder.ComputeSha256(PowerShellCompilationAbiBuilder.GetNormalizedText(abi));
        return abi;
    }
}
