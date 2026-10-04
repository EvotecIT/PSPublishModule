using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void InvocationMetadata_PreservesPinnedOfflineLogging(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PowerShellForGitHub", "InvocationMetadata", "Write-InvocationLog.ps1");
        Assert.Equal("CEFFA801F781837E6991F89527B799C341A66CA6A3B3A7C9C08D71FF838F8DEB",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
        var moduleSource = File.ReadAllText(source) + "\n" + """
            $script:alwaysExcludeParametersForLogging = @('InternalOnly')
            $script:alwaysRedactParametersForLogging = @('ApiToken')
            function Write-Log {
                [CmdletBinding()]param([string]$Message, [string]$Level)
                "$Level|$Message"
            }
            """;
        using var fixture = ArtifactFixture.Create(moduleSource, ".psm1");
        // The authored default logs its module invocation name; give the original
        // import the same package name as the generated module before comparing it.
        var originalModulePath = Path.Combine(fixture.RootPath, "Generated.InvocationMetadata.psm1");
        File.Copy(fixture.ScriptPath, originalModulePath);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.InvocationMetadata",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var entry = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Write-InvocationLog");
        Assert.True(entry.EmittedClrMethod, string.Join("; ", entry.DiagnosticChain.Select(cause => cause.Message)));
        Assert.True(entry.UsesNativeFunctionBinding);

        const string probe = """
            $ErrorActionPreference = 'Stop'
            function Invoke-LoggedCase {
                [CmdletBinding()]param([string]$ApiToken, [string]$SessionKey, [int]$Count, [switch]$Refresh)
                Write-InvocationLog -Invocation $MyInvocation -RedactParameter SessionKey -ExcludeParameter Count
            }
            $explicit = Invoke-LoggedCase -ApiToken 'secret-1' -SessionKey 'secret-3' -Count 4 -Refresh
            $default = & {
                [CmdletBinding()]param([string]$ApiToken)
                Write-InvocationLog -RedactParameter ApiToken
            } -ApiToken 'secret-2'
            $bad = ''
            try { Write-InvocationLog -Invocation 42 } catch { $bad = $_.FullyQualifiedErrorId }
            [pscustomobject]@{
                explicit = $explicit
                default = $default
                bad = $bad
                type = (Get-Command Write-InvocationLog).Parameters['Invocation'].ParameterType.FullName
            } | ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(originalModulePath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.True(original == generated,
            "Original: " + original + Environment.NewLine + "Generated: " + generated);
        using var observation = System.Text.Json.JsonDocument.Parse(original);
        var record = observation.RootElement;
        var explicitLog = record.GetProperty("explicit").GetString()!;
        var defaultLog = record.GetProperty("default").GetString()!;
        Assert.Contains("-ApiToken <redacted>", explicitLog);
        Assert.Contains("-SessionKey <redacted>", explicitLog);
        Assert.Contains("-Refresh:$true", explicitLog);
        Assert.DoesNotContain("secret-1", explicitLog);
        Assert.DoesNotContain("secret-3", explicitLog);
        Assert.DoesNotContain("-Count", explicitLog);
        Assert.Contains("Executing: Generated.InvocationMetadata", defaultLog);
        Assert.DoesNotContain("secret-2", defaultLog);
        Assert.Equal("System.Management.Automation.InvocationInfo", record.GetProperty("type").GetString());
        Assert.Contains("ParameterArgumentTransformationError", record.GetProperty("bad").GetString());
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void InvocationMetadata_RequiresNativeHostBinding(string framework)
    {
        var source = FindCompleteConversionWorkflow("PowerShellForGitHub", "InvocationMetadata", "Write-InvocationLog.ps1");
        var document = PowerShellSourceParser.Parse(File.ReadAllText(source), source);
        foreach (var capabilities in new[]
                 {
                     PowerShellCompilationCapabilities.TypedLibrary,
                     PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
                     PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
                 })
        {
            var compiled = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, capabilities);
            Assert.Empty(compiled.Emitted.Methods);
        }
    }
}
