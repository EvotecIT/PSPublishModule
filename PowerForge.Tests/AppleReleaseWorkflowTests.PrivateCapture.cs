namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PrivateCaptureSchedulesNativeWorkOnlyAfterHostedQualification()
    {
        var root = FindRepoRoot();
        var parser = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var workflow = parser.Deserialize<Dictionary<object, object>>(Read(root, ".github", "workflows", "powerforge-apple-private-screenshot-capture.yml"));
        var inputs = (Dictionary<object, object>)((Dictionary<object, object>)((Dictionary<object, object>)workflow["on"])["workflow_call"])["inputs"];
        foreach (var name in new[] { "source_repository", "source_branch", "qualification_workflow", "required_checks_json", "runner_group" })
            Assert.Equal("true", ((Dictionary<object, object>)inputs[name])["required"].ToString());
        Assert.False(inputs.ContainsKey("simulator_tool_ref"));
        var jobs = (Dictionary<object, object>)workflow["jobs"];
        var qualify = (Dictionary<object, object>)jobs["qualify"];
        var capture = (Dictionary<object, object>)jobs["capture"];
        Assert.Equal("ubuntu-latest", qualify["runs-on"]);
        Assert.False(qualify.ContainsKey("if"));
        Assert.Equal("qualify", capture["needs"]);
        Assert.Equal("${{ !cancelled() && needs.qualify.result == 'success' }}", capture["if"]);
        Assert.Contains("inputs.runner_group", (string)capture["runs-on"], StringComparison.Ordinal);
        var policySteps = ((List<object>)qualify["steps"]).Cast<Dictionary<object, object>>().ToArray();
        Assert.Equal("${{ toJSON(job) }}", ((Dictionary<object, object>)policySteps[0]["env"])["JOB_WORKFLOW"]);
        Assert.Equal("${{ steps.policy.outputs.owner_commit }}", ((Dictionary<object, object>)policySteps[1]["with"])["ref"]);
        var captureSteps = ((List<object>)capture["steps"]).Cast<Dictionary<object, object>>().ToArray();
        Assert.Equal("${{ needs.qualify.outputs.owner_commit }}", ((Dictionary<object, object>)captureSteps[0]["with"])["ref"]);
        Assert.Equal("./capture-owner/.github/actions/apple-screenshot-capture", captureSteps[1]["uses"]);
        Assert.Equal("${{ needs.qualify.outputs.owner_commit }}", ((Dictionary<object, object>)captureSteps[1]["with"])["simulator_tool_ref"]);

        var legacy = parser.Deserialize<Dictionary<object, object>>(Read(root, ".github", "workflows", "powerforge-apple-screenshot-capture.yml"));
        var legacyInputs = (Dictionary<object, object>)((Dictionary<object, object>)((Dictionary<object, object>)legacy["on"])["workflow_call"])["inputs"];
        Assert.False(legacyInputs.ContainsKey("runner_group"));
        Assert.False(legacyInputs.ContainsKey("source_repository"));
        var legacyCapture = (Dictionary<object, object>)((Dictionary<object, object>)legacy["jobs"])["capture"];
        var legacySteps = ((List<object>)legacyCapture["steps"]).Cast<Dictionary<object, object>>().ToArray();
        Assert.Equal(captureSteps[1]["uses"], legacySteps[2]["uses"]);
        Assert.False(((Dictionary<object, object>)legacySteps[2]["with"]).ContainsKey("source_repository"));
    }

    [Fact]
    public void PrivateCaptureAuthenticatesWorkflowIdentityBeforeLoadingOwnerCode()
    {
        var root = FindRepoRoot();
        var workflow = new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(Read(root, ".github", "workflows", "powerforge-apple-private-screenshot-capture.yml"));
        var qualify = (Dictionary<object, object>)((Dictionary<object, object>)workflow["jobs"])["qualify"];
        var first = (Dictionary<object, object>)((List<object>)qualify["steps"])[0];
        var fixture = Path.Combine(Path.GetTempPath(), $"powerforge-capture-identity-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(fixture);
            var gate = Path.Combine(fixture, "gate.ps1");
            File.WriteAllText(gate, "$ErrorActionPreference = 'Stop'\n" + (string)first["run"]);
            var runner = Path.Combine(fixture, "exercise.ps1");
            File.WriteAllText(runner, """
                param($Gate, $Fixture)
                $ErrorActionPreference = 'Stop'
                $env:GITHUB_OUTPUT = Join-Path $Fixture 'output.txt'
                $env:SOURCE_REPOSITORY = 'Synthetic/PublicApp'; $env:RUNNER_GROUP = 'private-capture'
                $env:RUNNER_LABELS = '["private-capture"]'
                $identity = @{ workflow_repository = 'EvotecIT/PSPublishModule'; workflow_file_path = '.github/workflows/powerforge-apple-private-screenshot-capture.yml'; workflow_sha = 'a' * 40 }
                $env:JOB_WORKFLOW = $identity | ConvertTo-Json -Compress
                & $Gate
                if ((Get-Content $env:GITHUB_OUTPUT -Raw).Trim() -cne ('owner_commit=' + ('a' * 40))) { throw 'Wrong executing owner identity.' }
                foreach ($mutate in @(
                    { $env:SOURCE_REPOSITORY = '' },
                    { $identity.workflow_repository = 'Other/Owner' },
                    { $identity.workflow_file_path = '.github/workflows/powerforge-apple-screenshot-capture.yml' },
                    { $identity.workflow_sha = 'unmerged-input-ref' },
                    { $identity.Remove('workflow_sha') },
                    { $env:RUNNER_GROUP = 'Default' }
                )) {
                    $identity.workflow_repository = 'EvotecIT/PSPublishModule'
                    $identity.workflow_file_path = '.github/workflows/powerforge-apple-private-screenshot-capture.yml'
                    $identity.workflow_sha = 'a' * 40
                    $env:SOURCE_REPOSITORY = 'Synthetic/PublicApp'; $env:RUNNER_GROUP = 'private-capture'
                    & $mutate
                    $env:JOB_WORKFLOW = $identity | ConvertTo-Json -Compress
                    $rejected = $false
                    try { & $Gate } catch { $rejected = $true }
                    if (-not $rejected) { throw 'Unsafe identity or legacy skip route reached checkout.' }
                }
                'PRIVATE_CAPTURE_IDENTITY_PASS'
                """);
            var result = Run("pwsh", fixture, "-NoLogo", "-NoProfile", "-File", runner, gate, fixture);
            result.EnsureSuccess();
            Assert.Contains("PRIVATE_CAPTURE_IDENTITY_PASS", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { DeletePinnedEvidenceDirectory(fixture); }
    }

    private static string ReadCaptureImplementation(string root) =>
        Read(root, ".github", "workflows", "powerforge-apple-screenshot-capture.yml") +
        Read(root, ".github", "actions", "apple-screenshot-capture", "action.yml");

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
