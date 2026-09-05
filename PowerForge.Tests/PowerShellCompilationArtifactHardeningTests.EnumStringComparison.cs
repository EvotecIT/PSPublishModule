using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("-eq", "PowerShellEqualIgnoreCase", "true", false)]
    [InlineData("-ne", "PowerShellNotEqualIgnoreCase", "true", true)]
    [InlineData("-ceq", "PowerShellEqualCaseSensitive", "false", false)]
    [InlineData("-cne", "PowerShellNotEqualCaseSensitive", "false", true)]
    public void HostedEnumStringEqualityUsesCanonicalPowerShellOperator(
        string operation,
        string expectedOperator,
        string expectedIgnoreCase,
        bool expectedNegation)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Test-EnumText {{ param([System.DayOfWeek] $Value, [string] $Name) return $Value {operation} $Name }}",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "enum-string-comparison.ps1"));

        var unsupported = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(unsupported.Emitted.Methods);
        Assert.Contains(unsupported.Emitted.Diagnostics, static diagnostic => diagnostic.Code == "PSL1002");

        var supported = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);
        Assert.Empty(supported.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(supported.Analyzed.Functions);
        Assert.True(function.Capabilities.HasFlag(PowerShellRequiredCapability.PowerShellLanguageOperators));
        var bound = Assert.IsType<PowerShellBoundBinaryExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(function.Body.Statements)).Expression);
        Assert.Equal(expectedOperator, bound.Operation.ToString());
        var lowered = Assert.IsType<PowerShellLoweredBinaryExpression>(
            Assert.IsType<PowerShellLoweredReturnStatement>(Assert.Single(Assert.Single(supported.Lowered.Functions).Statements)).Expression);
        Assert.Equal(expectedOperator, lowered.Operation.ToString());
        var source = Assert.Single(supported.Emitted.Methods).Source;
        Assert.Contains("System.Management.Automation.LanguagePrimitives.Equals", source, StringComparison.Ordinal);
        Assert.Contains($", {expectedIgnoreCase}, global::System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
        Assert.Equal(expectedNegation, source.Contains("!(global::System.Management.Automation.LanguagePrimitives.Equals", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("return $Name -eq [System.DayOfWeek]::Monday")]
    [InlineData("return $Value -lt 'Monday'")]
    [InlineData("return $Value -eq [object] 'Monday'")]
    public void EnumStringComparisonDoesNotBroadenBeyondLeftDirectedScalarEquality(string expression)
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-EnumText { param([System.DayOfWeek] $Value, [string] $Name) " + expression + " }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "unsupported-enum-string-comparison.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Fact]
    public void BinaryTranspilerComposesLiveLanguageModeWithEnumStringEquality()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-LanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq 'fulllanguage' }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.EnumStringComparison",
            "CompiledPowerShell",
            "net10.0");

        var method = Assert.Single(typed.Methods);
        Assert.Empty(typed.Diagnostics);
        Assert.True(method.RequiresPowerShellRuntimeState);
        Assert.Contains("LanguagePrimitives.Equals", typed.SourceCode, StringComparison.Ordinal);
        Assert.Contains("__runtimeState[\"LanguageMode\"]", typed.SourceCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void BinaryModulePreservesLiveEnumStringEquality(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            "function Test-LanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq 'fulllanguage' }; " +
            "function Test-CaseLanguageMode { return $ExecutionContext.SessionState.LanguageMode -ceq 'fulllanguage' }; " +
            "function Test-OtherLanguageMode { return $ExecutionContext.SessionState.LanguageMode -ne 'RestrictedLanguage' }; " +
            "function Test-NumericLanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq '0' }; " +
            "function Test-InvalidLanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq 'NotAMode' }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.EnumStringComparison",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string calls = "Test-LanguageMode; Test-CaseLanguageMode; Test-OtherLanguageMode; Test-NumericLanguageMode; Test-InvalidLanguageMode";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal((0, original.StandardOutput.Trim(), string.Empty),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
        Assert.Equal(
            string.Join(Environment.NewLine, "True", "True", "True", "True", "False"),
            compiled.StandardOutput.Trim());
    }

    [PinnedSemanticHostFact]
    public void Build_BinaryModuleLanguageModeMatchesConfiguredExactProfiles()
    {
        var powerShell74Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH74_PATH");
        var powerShell76Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(powerShell74Path));
        Assert.False(string.IsNullOrWhiteSpace(powerShell76Path));
        var profiles = new[]
        {
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, "net472", "powershell.exe"),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId, "net8.0", powerShell74Path!),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, "net10.0", powerShell76Path!)
        };
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get(
            "PowerForge.Semantic/runtime-language-mode");

        foreach (var (profileId, targetFramework, host) in profiles)
        {
            var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profileId);
            using var oracleFixture = ArtifactFixture.Create(
                PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId) + Environment.NewLine +
                "$ExecutionContext.SessionState.LanguageMode -eq 'fulllanguage'" + Environment.NewLine +
                "$ExecutionContext.SessionState.LanguageMode -ceq 'fulllanguage'" + Environment.NewLine +
                "$ExecutionContext.SessionState.LanguageMode -ne 'RestrictedLanguage'" + Environment.NewLine +
                "$ExecutionContext.SessionState.LanguageMode -eq '0'" + Environment.NewLine +
                "$ExecutionContext.SessionState.LanguageMode -eq 'NotAMode'");
            var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
                new PowerShellCompilationSemanticOracleRequest(profileId, oracleFixture.ScriptPath)
                {
                    Culture = "en-US",
                    HostExecutablePath = host == "powershell.exe" ? null : host,
                    ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
                });
            Assert.Equal(
                new[] { "FullLanguage", "True", "True", "True", "True", "False" },
                interpreted.Success.Select(static observation => observation.Value));
            Assert.Equal("System.String", interpreted.Success[0].TypeName);
            Assert.All(interpreted.Success.Skip(1), static observation => Assert.Equal("System.Boolean", observation.TypeName));

            using var moduleFixture = ArtifactFixture.Create(
                "function Get-LanguageMode { return [string] $ExecutionContext.SessionState.LanguageMode }; " +
                "function Test-LanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq 'fulllanguage' }; " +
                "function Test-CaseLanguageMode { return $ExecutionContext.SessionState.LanguageMode -ceq 'fulllanguage' }; " +
                "function Test-OtherLanguageMode { return $ExecutionContext.SessionState.LanguageMode -ne 'RestrictedLanguage' }; " +
                "function Test-NumericLanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq '0' }; " +
                "function Test-InvalidLanguageMode { return $ExecutionContext.SessionState.LanguageMode -eq 'NotAMode' }",
                ".psm1");
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                moduleFixture.ScriptPath,
                moduleFixture.OutputPath,
                "PowerForge.ExactLanguageModeState",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = targetFramework,
                SemanticProfileId = profileId
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.True(result.Manifest!.RequiresPowerShellRuntime);
            var compiled = Run(
                host,
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; " +
                "Get-LanguageMode; Test-LanguageMode; Test-CaseLanguageMode; Test-OtherLanguageMode; Test-NumericLanguageMode; Test-InvalidLanguageMode");
            Assert.Equal(0, compiled.ExitCode);
            Assert.Equal(string.Empty, compiled.StandardError.Trim());
            Assert.Equal(
                interpreted.Success.Select(static observation => observation.Value),
                compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
