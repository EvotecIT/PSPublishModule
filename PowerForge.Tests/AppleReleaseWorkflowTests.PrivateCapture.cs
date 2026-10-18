namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PrivateCaptureSchedulesNativeWorkOnlyAfterHostedQualification()
    {
        var workflow = new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(Read(FindRepoRoot(), ".github", "workflows", "powerforge-apple-screenshot-capture.yml"));
        var jobs = (Dictionary<object, object>)workflow["jobs"];
        var qualify = (Dictionary<object, object>)jobs["qualify"];
        var capture = (Dictionary<object, object>)jobs["capture"];
        Assert.Equal("ubuntu-latest", qualify["runs-on"]);
        Assert.Equal("inputs.source_repository != ''", qualify["if"]);
        Assert.Equal("qualify", capture["needs"]);
        Assert.Equal("${{ !cancelled() && (needs.qualify.result == 'success' || (inputs.source_repository == '' && needs.qualify.result == 'skipped')) }}", capture["if"]);
        Assert.Contains("inputs.runner_group", (string)capture["runs-on"], StringComparison.Ordinal);
        var steps = (List<object>)capture["steps"];
        var checkout = steps.Cast<Dictionary<object, object>>().Single(step =>
            step.TryGetValue("id", out var id) && (string)id == "source_checkout");
        Assert.Equal("${{ inputs.source_repository || github.repository }}",
            ((Dictionary<object, object>)checkout["with"])["repository"]);
    }

    [Fact]
    public void PrivateCaptureRejectsUnqualifiedSourceAndUnenrolledControllers()
    {
        var root = FindRepoRoot();
        var fixture = Path.Combine(Path.GetTempPath(), $"powerforge-private-capture-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(fixture);
            var result = Run("pwsh", fixture, "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "PowerForge.Tests", "Fixtures", "AppleCapture", "PrivateCapture.ps1"),
                "-Root", root, "-Fixture", fixture);
            result.EnsureSuccess();
            Assert.Contains("PRIVATE_CAPTURE_PASS", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(fixture); }
    }
}
