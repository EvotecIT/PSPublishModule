using System.Globalization;
using YamlDotNet.Serialization;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData("approval", "approve", "Execute the approved transition")]
    [InlineData("governance", "apply", "Apply only the reviewed Apple governance plan")]
    [InlineData("screenshots", "approve-and-sync", "Sync approved screenshots to App Store Connect")]
    [InlineData("screenshot-approve", "approve-and-sync", "Sync manually approved screenshots to App Store Connect")]
    public void ProtectedAppleMutationRequiresUnexpiredReview(string workflow, string job, string mutation)
    {
        var root = FindRepoRoot();
        var yaml = Read(root, ".github", "workflows", $"powerforge-apple-{workflow}.yml");
        var document = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
        var jobs = (Dictionary<object, object>)document["jobs"];
        var approval = (Dictionary<object, object>)jobs[job];
        var steps = ((IEnumerable<object>)approval["steps"]).Cast<Dictionary<object, object>>().ToList();
        var mutationIndex = steps.FindIndex(step => step.TryGetValue("name", out var name) && Equals(name, mutation));
        Assert.True(mutationIndex > 0, $"The protected mutation step was not found in {workflow}.");
        var guard = steps[mutationIndex - 1];
        Assert.Equal("Require fresh review evidence", guard["name"]);
        Assert.False(guard.ContainsKey("if"));
        Assert.False(steps[mutationIndex].ContainsKey("if"));
        var recoveryUploads = steps.Skip(mutationIndex + 1).Where(step =>
            step.TryGetValue("uses", out var uses) && uses is string action &&
            action.StartsWith("actions/upload-artifact@", StringComparison.Ordinal));
        Assert.NotEmpty(recoveryUploads);
        Assert.All(recoveryUploads, upload => Assert.Equal(
            "${{ always() && steps.review-freshness.outcome == 'success' }}", upload["if"]));

        if (workflow == "screenshots")
        {
            var capture = (Dictionary<object, object>)jobs["capture"];
            var captureSteps = ((IEnumerable<object>)capture["steps"]).Cast<Dictionary<object, object>>();
            var provenance = Assert.Single(captureSteps, step =>
                step.TryGetValue("name", out var name) && Equals(name, "Retain compact capture provenance"));
            var pendingPngs = Assert.Single(captureSteps, step =>
                step.TryGetValue("name", out var name) && Equals(name, "Retain captures for protected review"));
            Assert.Equal("30", ((Dictionary<object, object>)provenance["with"])["retention-days"].ToString());
            Assert.Equal("7", ((Dictionary<object, object>)pendingPngs["with"])["retention-days"].ToString());
        }

        if (!CommandExists("pwsh")) return;

        var script = (string)guard["run"];
        var now = DateTimeOffset.UtcNow;
        var environment = new Dictionary<string, string?>
        {
            ["REVIEW_EXPIRES_AT"] = now.AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };
        RunWithEnvironment("pwsh", root, environment, "-NoProfile", "-Command", script).EnsureSuccess();

        foreach (var expiry in new[] { now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), null })
        {
            environment["REVIEW_EXPIRES_AT"] = expiry;
            var result = RunWithEnvironment("pwsh", root, environment, "-NoProfile", "-Command", script);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("seven-day", result.StandardError, StringComparison.Ordinal);
        }
    }
}
