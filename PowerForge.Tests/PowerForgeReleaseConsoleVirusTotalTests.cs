using PowerForge.ConsoleUi;

namespace PowerForge.Tests;

[Collection(ConsoleCapture.Collection)]
public sealed class PowerForgeReleaseConsoleVirusTotalTests
{
    [Fact]
    public void InteractiveRelease_VirusTotalPublication_ShowsPhaseAndFailedOutcome()
    {
        using var writer = new StringWriter();
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions
            {
                ModuleName = "Example",
                ModuleVersion = "1.2.3"
            },
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
                ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
            }
        };
        var request = new PowerForgeReleaseRequest
        {
            ConfigPath = "release.json",
            ModuleOnly = true,
            ModuleRunMode = ConfigurationGateMode.Publish
        };

        var result = Run(writer,
            spec,
            request,
            progress =>
            {
                progress.PhaseStarted(PowerForgeReleaseProgressPhase.VirusTotal, 1, "registering");
                progress.PhaseFailed(PowerForgeReleaseProgressPhase.VirusTotal, "Monitor unavailable");
                return new PowerForgeReleaseResult
                {
                    Success = true,
                    VirusTotalMonitor = new VirusTotalMonitorPublishResult
                    {
                        Success = false,
                        ErrorMessage = "Monitor unavailable"
                    }
                };
            });
        WriteSummary(writer, result, TimeSpan.FromSeconds(1));

        Assert.True(result.Success);
        var output = writer.ToString();
        Assert.Contains("VirusTotal Monitor registration", output, StringComparison.Ordinal);
        Assert.Contains("Monitor unavailable", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseSummary_NoMatchingVirusTotalArtifacts_ShowsSkippedOutcome()
    {
        using var writer = new StringWriter();

        WriteSummary(writer,
            new PowerForgeReleaseResult
            {
                Success = true,
                VirusTotalMonitor = new VirusTotalMonitorPublishResult
                {
                    Success = true,
                    Artifacts = []
                }
            },
            TimeSpan.FromSeconds(1));

        var output = writer.ToString();
        Assert.Contains("VirusTotal Monitor", output, StringComparison.Ordinal);
        Assert.Contains("Skipped", output, StringComparison.Ordinal);
        Assert.Contains("no configured final release artifacts matched", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveRelease_AppleOnlyTarget_DoesNotShowVirusTotalPhase()
    {
        using var writer = new StringWriter();
        var spec = new PowerForgeReleaseSpec
        {
            Tools = new PowerForgeToolReleaseSpec
            {
                GitHub = new PowerForgeToolReleaseGitHubOptions { Publish = true },
                Targets = [new PowerForgeToolReleaseTarget { Name = "WindowsTool" }]
            },
            AppleApps = new PowerForgeAppleReleaseOptions
            {
                Apps = [new AppleAppConfiguration { Enabled = true, Name = "MacApp" }]
            },
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
                ArtifactKinds = [VirusTotalArtifactKind.Executable]
            }
        };
        var request = new PowerForgeReleaseRequest
        {
            ConfigPath = "release.json",
            Targets = ["MacApp"]
        };

        _ = Run(writer,
            spec,
            request,
            _ => new PowerForgeReleaseResult { Success = true });

        Assert.DoesNotContain("VirusTotal Monitor registration", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveRelease_AppleOnlySwitch_DoesNotShowVirusTotalPhase()
    {
        using var writer = new StringWriter();
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions
            {
                ModuleName = "Example",
                IncludesPackages = true
            },
            Packages = new ProjectBuildConfiguration { PublishNuget = true },
            AppleApps = new PowerForgeAppleReleaseOptions
            {
                Apps = [new AppleAppConfiguration { Enabled = true, Name = "MacApp" }]
            },
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
                ArtifactKinds = [VirusTotalArtifactKind.NuGetPackage]
            }
        };
        var request = new PowerForgeReleaseRequest
        {
            ConfigPath = "release.json",
            AppleOnly = true
        };

        _ = Run(writer,
            spec,
            request,
            _ => new PowerForgeReleaseResult { Success = true });

        Assert.DoesNotContain("VirusTotal Monitor registration", writer.ToString(), StringComparison.Ordinal);
    }

    private static PowerForgeReleaseResult Run(
        StringWriter writer,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request,
        Func<IPowerForgeReleaseProgressReporter, PowerForgeReleaseResult> run)
    {
        PowerForgeReleaseResult? result = null;
        writer.Write(ConsoleCapture.Final(view => result = PowerForgeReleaseConsoleUi.RunInteractive(view, spec, request, run)));
        return result!;
    }

    private static void WriteSummary(StringWriter writer, PowerForgeReleaseResult result, TimeSpan duration)
        => writer.Write(ConsoleCapture.Final(_ => PowerForgeReleaseConsoleUi.WriteSummary(result, duration)));
}
