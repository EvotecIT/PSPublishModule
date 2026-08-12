using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseConsoleVirusTotalTests
{
    [Fact]
    public void InteractiveRelease_VirusTotalPublication_ShowsPhaseAndFailedOutcome()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
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

        var result = SpectrePowerForgeReleaseConsoleUi.RunInteractive(
            console,
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
        SpectrePowerForgeReleaseConsoleUi.WriteSummary(console, result, TimeSpan.FromSeconds(1));

        Assert.True(result.Success);
        var output = writer.ToString();
        Assert.Contains("VirusTotal Monitor registration", output, StringComparison.Ordinal);
        Assert.Contains("Monitor unavailable", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseSummary_NoMatchingVirusTotalArtifacts_ShowsSkippedOutcome()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);

        SpectrePowerForgeReleaseConsoleUi.WriteSummary(
            console,
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
        var console = CreateConsole(writer);
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

        _ = SpectrePowerForgeReleaseConsoleUi.RunInteractive(
            console,
            spec,
            request,
            _ => new PowerForgeReleaseResult { Success = true });

        Assert.DoesNotContain("VirusTotal Monitor registration", writer.ToString(), StringComparison.Ordinal);
    }

    private static IAnsiConsole CreateConsole(TextWriter writer)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
}
