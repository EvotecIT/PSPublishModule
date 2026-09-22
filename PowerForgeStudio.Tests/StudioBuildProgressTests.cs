using PowerForge;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class StudioBuildProgressTests
{
    [Fact]
    public void ProgressPreservesPhaseAndRedactsRecognizedSecretArguments()
    {
        var values = new List<ReleaseBuildProgress>();
        var reporter = new ReleaseBuildProgressAdapter(new InlineProgress(values.Add));
        reporter.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 1, "packing --ApiKey secret-value");
        var value = Assert.Single(values);
        Assert.Equal("PackageBuild", value.Phase);
        Assert.Equal("Started", value.State);
        Assert.DoesNotContain("secret-value", value.Detail);
        Assert.Contains("<redacted>", value.Detail);
        Assert.Equal(4096, StudioOutputSanitizer.Sanitize(new string('x', 5000)).Length);
    }

    [Fact]
    public void PublicationAddressSanitizerOmitsCredentialsFromDestinationAndDiagnostic()
    {
        const string address = "https://feed-user:feed-password@packages.example.test/v3/index.json?token=query-secret#fragment-secret";
        const string safe = "https://packages.example.test/v3/index.json";

        Assert.Equal(safe, StudioOutputSanitizer.SanitizeDestination(address));
        var diagnostic = StudioOutputSanitizer.Sanitize($"Push failed at {address} (403).");
        Assert.Contains(safe, diagnostic);
        Assert.DoesNotContain("feed-password", diagnostic);
        Assert.DoesNotContain("query-secret", diagnostic);
        Assert.DoesNotContain("fragment-secret", diagnostic);
        Assert.Equal("Invalid publication URL (details omitted)",
            StudioOutputSanitizer.SanitizeDestination("https://feed-user:feed-password@"));
        Assert.Equal("Invalid publication URL (details omitted)",
            StudioOutputSanitizer.SanitizeDestination("https:/feed-user:feed-password@packages.example.test?token=query-secret"));
        Assert.DoesNotContain("query-secret", StudioOutputSanitizer.Sanitize("Publish failed at https:/feed-user:feed-password@packages.example.test?token=query-secret"));
        Assert.Equal(@"C:\Work\release.json", StudioOutputSanitizer.SanitizeDestination(@"C:\Work\release.json"));
    }

    private sealed class InlineProgress(Action<ReleaseBuildProgress> report) : IProgress<ReleaseBuildProgress>
    {
        public void Report(ReleaseBuildProgress value) => report(value);
    }
}
