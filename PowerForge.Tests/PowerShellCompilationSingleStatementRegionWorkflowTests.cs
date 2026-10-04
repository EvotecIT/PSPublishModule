using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedIpConversionUsesNativeMatchWithoutChangingBehavior(
        string framework,
        string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods",
            "FullModule",
            "Private",
            "Convert-IPToBinary.ps1");
        Assert.Equal(
            "cd183e6262bd24eeb7adc8ce471cf60de63a214d918c0a490336e1e77f58e9e3",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function Convert-IPToBinary" + Environment.NewLine,
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "PinnedIpConversionMethods",
            framework,
            PowerShellCompilationCapabilities.HybridModule);
        var method = Assert.Single(typed.Methods, static method => method.SourceName == "Convert-IPToBinary");
        Assert.NotNull(method.NativeFunctionBinding);
        Assert.Empty(typed.PromotedRegions);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PinnedIpConversion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework, EmitSource = true });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.PromotedTypedRegions);
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Convert-IPToBinary");
        Assert.True(unit.EmittedClrMethod);
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.Equal(0, unit.PromotedTypedRegions);
        var methodAbi = Assert.Single(PowerShellCompilationAbiBuilder.Create(
            typed.NamespaceName, typed.TypeName, new[] { method }).Methods);
        var nativeContext = Assert.Single(methodAbi.Parameters,
            static parameter => parameter.CompilerPurpose == "NativeFunctionContext");
        Assert.Equal("__nativeFunction", nativeContext.ClrName);
        Assert.Equal("PowerForge.Generated.Runtime.PowerShellNativeFunctionContext", nativeContext.TypeName);
        Assert.DoesNotContain(methodAbi.Parameters,
            static parameter => parameter.PowerShellName.Equals("IP", StringComparison.OrdinalIgnoreCase));
        var generatedSource = string.Join(Environment.NewLine, Directory.EnumerateFiles(
            result.GeneratedSourcePath!, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("<param name=\"__nativeFunction\">", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<param name=\"IP\">", generatedSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.RootPath, generatedSource, StringComparison.OrdinalIgnoreCase);

        const string probe = """
            function Describe-RegionRecord($record) {
                if ($record -is [Management.Automation.ErrorRecord]) {
                    'error:' + $record.FullyQualifiedErrorId + ':' + $record.Exception.Message
                } elseif ($record -is [Management.Automation.WarningRecord]) {
                    'warning:' + $record.Message
                } else {
                    'value:' + $record.GetType().FullName + ':' + [string]$record
                }
            }
            $cases = @(
                @{ Name = 'ordinary'; Value = '192.168.1.1' },
                @{ Name = 'trimmed'; Value = ' 10.0.0.1 ' },
                @{ Name = 'invalid'; Value = '300.1.1.1' },
                @{ Name = 'empty'; Value = '' },
                @{ Name = 'null'; Value = $null }
            )
            foreach ($case in $cases) {
                $Error.Clear()
                $records = @(Convert-IPToBinary -IP $case.Value 3>&1 2>&1 | ForEach-Object { Describe-RegionRecord $_ })
                $errors = @($Error | ForEach-Object { Describe-RegionRecord $_ })
                [pscustomobject]@{ Case = $case.Name; Records = $records; Errors = $errors } |
                    ConvertTo-Json -Depth 5 -Compress
            }
            $first = @(& { foreach ($value in 1..200) { Convert-IPToBinary -IP '127.0.0.1' } } | Select-Object -First 1)
            'stopped=' + ($first -join ',')
            'reuse=' + (Convert-IPToBinary -IP '8.8.8.8')
            Remove-Module $module.Name
            $module = Import-Module $modulePath -PassThru
            'reimport=' + (Convert-IPToBinary -IP '1.1.1.1')
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "pinned-ip-conversion");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "pinned-ip-conversion");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("stopped=01111111000000000000000000000001", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reuse=00001000000010000000100000001000", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reimport=00000001000000010000000100000001", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
