using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests {
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Execute_legacy_cached_publication_binds_the_coordinator_fingerprint_in_the_real_child(int replacement, bool wrapperChoosesReuse) {
        string root = CreateSandbox();
        try {
            string module = Directory.CreateDirectory(Path.Combine(root, "Sample")).FullName;
            string manifest = Path.Combine(module, "Sample.psd1");
            File.WriteAllText(manifest, "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.2.3'; FunctionsToExport = @('Get-Sample'); CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(module, "Sample.psm1"), "function Get-Sample { 'ok' }");
            string config = Path.Combine(root, "cached-module.json");
            string sourceModule = Path.Combine(module, "Sample.psm1");
            File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(new {
                Build = new { Name = "Sample", SourcePath = "Sample", Version = "1.2.3", KeepStaging = true },
                Install = new { Enabled = false },
                Segments = new[] { new { Type = "Execute", Configuration = new {
                    At = "BeforeDependencies",
                    InlineScript = $"[IO.File]::AppendAllText('{sourceModule.Replace("'", "''")}', [Environment]::NewLine + '# build lifecycle fixture' + [Environment]::NewLine)"
                } } }
            }));
            string script = Path.Combine(root, "Build-Module.ps1");
            string childFingerprint = Path.Combine(root, "child-fingerprint.txt");
            File.WriteAllText(script, $$"""
                [CmdletBinding()]
                param(
                    [string] $RunMode,
                    [string] $ModuleVersion,
                    [string] $PreReleaseTag,
                    [bool] $NoDotnetBuild,
                    [string] $StagingPath,
                    [switch] $ReuseStaging,
                    [bool] $IncludeProjectPackages,
                    [bool] $IncludeModulePublishing,
                    [switch] $SkipInstall,
                    [switch] $NoSign
                )
                if ($RunMode -eq 'Publish') {
                    [IO.File]::WriteAllText('{{childFingerprint.Replace("'", "''")}}', [string]$env:POWERFORGE_MODULE_PUBLICATION_CHECKPOINT_SHA256)
                    # The cached wrapper selects reuse itself; the outer request does not.
                    $ReuseStaging = {{(wrapperChoosesReuse ? "$true" : "$false")}}
                }
                Invoke-ModuleBuild -ConfigPath '{{config.Replace("'", "''")}}' -RunMode $RunMode `
                    -StagingPath $StagingPath -ReuseStaging:$ReuseStaging -NoDotnetBuild -NoSign -SkipInstall -PassThru
                """);
            string validation = Path.Combine(root, "validate.ps1");
            File.WriteAllText(validation, "# validation boundary");
            string release = Path.Combine(root, "release.json");
            File.WriteAllText(release, "{}");
            string hostModule = Path.Combine(RepoRootLocator.Find(), "PSPublishModule", "bin", "Release", "net8.0", "PSPublishModule.dll");
            Assert.True(File.Exists(hostModule), hostModule);
            var host = new ModuleBuildHostService();
            byte[]? original = null;
            byte[]? finalizedSource = null;
            string? expected = null;
            string? forwarded = null;
            string? checkpoint = null;
            int calls = 0;
            bool validated = false;
            var service = new PowerForgeReleaseService(new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("No packages configured."),
                planTools: (_, _, _) => throw new InvalidOperationException("No tools configured."),
                runTools: _ => throw new InvalidOperationException("No tools configured."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("No tools configured."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("No tools configured."),
                runDotNetTools: _ => throw new InvalidOperationException("No tools configured."),
                publishGitHubRelease: _ => throw new InvalidOperationException("No network publication configured."),
                runReleaseValidation: (_, _, _, _) => {
                    validated = true;
                    return new PowerForgeReleaseValidationResult { Succeeded = true, ExitCode = 0 };
                },
                executeModuleBuild: (request, cancellation) => {
                    calls++;
                    Assert.False(request.ReuseStaging);
                    Assert.False(string.IsNullOrWhiteSpace(request.StagingPath));
                    if (request.RunMode == ConfigurationGateMode.Publish) {
                        Assert.True(validated);
                        forwarded = request.PublicationCheckpointSha256;
                        if (replacement == 1) File.AppendAllText(checkpoint!, "\n ");
                        if (replacement == 2) File.WriteAllBytes(checkpoint!, original!);
                    }
                    var hostResult = host.ExecuteBuildAsync(request, cancellation).GetAwaiter().GetResult();
                    if (request.RunMode == ConfigurationGateMode.Build) {
                        Assert.True(hostResult.Succeeded, hostResult.StandardError);
                        checkpoint = request.StagingPath + ".powerforge-publication.json";
                        original = File.ReadAllBytes(checkpoint);
                        finalizedSource = File.ReadAllBytes(sourceModule);
                        expected = Convert.ToHexString(SHA256.HashData(original));
                        if (replacement == 2) {
                            File.Delete(checkpoint);
                            expected = "missing";
                        }
                    }
                    return hostResult;
                });
            var result = service.Execute(new PowerForgeReleaseSpec {
                Module = new PowerForgeModuleReleaseOptions {
                    RepositoryRoot = root, ScriptPath = script, ManifestPath = manifest, ModulePath = hostModule, Framework = "net8.0"
                },
                Validation = new PowerForgeReleaseValidationOptions {
                    AfterStaging = [new PowerForgeReleaseValidationAction { Name = "release", FilePath = validation }]
                }
            }, new PowerForgeReleaseRequest {
                ConfigPath = release, StageRoot = Path.Combine(root, "release-staged"), ModuleOnly = true, ModuleRunMode = ConfigurationGateMode.Publish,
                ModuleNoSign = true, ModuleNoDotnetBuild = true, ModuleSkipInstall = true
            });

            Assert.NotNull(finalizedSource);
            Assert.Equal(finalizedSource, File.ReadAllBytes(sourceModule));
            if (replacement == 0) {
                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(result.ModulePublication!.Succeeded, result.ModulePublication.StandardError);
            } else {
                Assert.False(result.Success);
                Assert.Contains("changed a release input", result.ModulePublication!.StandardError, StringComparison.Ordinal);
            }
            Assert.Equal(2, calls);
            Assert.NotNull(expected);
            Assert.Equal(expected, forwarded);
            Assert.Equal(expected, File.ReadAllText(childFingerprint));
        } finally {
            TryDelete(root);
        }
    }
}
