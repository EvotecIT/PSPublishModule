using YamlDotNet.Serialization;

namespace PowerForge.Tests;

public sealed class LinuxRunnerRootCapacityWorkflowTests
{
    private static readonly KeyValuePair<string, string>[] ExpectedRunners =
    {
        new("github-runner-linux", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux\"]"),
        new("github-runner-linux-01", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux-01\"]"),
        new("github-runner-linux-02", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux-02\"]"),
        new("github-runner-linux-03", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux-03\"]"),
        new("github-runner-linux-04", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux-04\"]"),
        new("github-runner-linux-05", "[\"self-hosted\",\"linux\",\"runner-github-runner-linux-05\"]")
    };

    [Fact]
    public void WorkflowIsManualOnlyAndRequiresFleetValidationBeforeApply()
    {
        var workflow = ReadWorkflow();
        var triggers = Mapping(workflow["on"]);
        var jobs = Mapping(workflow["jobs"]);

        Assert.Equal(new[] { "workflow_dispatch" }, triggers.Keys.Cast<string>());
        Assert.Equal(new[] { "validate-root", "expand-root" }, jobs.Keys.Cast<string>());

        var validationJob = Mapping(jobs["validate-root"]);
        var applyJob = Mapping(jobs["expand-root"]);
        Assert.Equal(ExpectedRunners, ReadMatrixRunners(validationJob));
        Assert.Equal(ExpectedRunners, ReadMatrixRunners(applyJob));
        Assert.Equal("validate-root", applyJob["needs"]);
        Assert.Equal("${{ inputs.apply == 'true' && needs.validate-root.result == 'success' }}", applyJob["if"]);
        Assert.Equal("${{ fromJSON(matrix.runner.labels) }}", validationJob["runs-on"]);
        Assert.Equal("${{ fromJSON(matrix.runner.labels) }}", applyJob["runs-on"]);

        AssertPinnedCheckout(validationJob);
        AssertPinnedCheckout(applyJob);
        AssertCapacityStep(validationJob, "Validate root expansion plan", "false", expectedConfirmation: null);
        AssertCapacityStep(applyJob, "Expand root volume", "true", "${{ inputs.confirmation }}");
    }

    [Fact]
    public void ExpansionScriptFailsClosedAndHasAConvergentExt4RecoveryPath()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, ".github", "scripts", "expand-linux-runner-root.sh");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("[[ \"$EUID\" -eq 0 ]]", script, StringComparison.Ordinal);
        Assert.Contains("RUNNER_NAME\" == \"$expected_runner_name", script, StringComparison.Ordinal);
        Assert.Contains("pgrep -xc Runner.Worker", script, StringComparison.Ordinal);
        Assert.Contains("flock -n 9", script, StringComparison.Ordinal);
        Assert.Contains("root_source=\"$(trim \"$(findmnt -n -o SOURCE /)\")\"", script, StringComparison.Ordinal);
        Assert.Contains("root_filesystem=\"$(trim \"$(findmnt -n -o FSTYPE /)\")\"", script, StringComparison.Ordinal);
        Assert.Contains("root_major_minor=\"$(trim \"$(findmnt -n -o MAJ:MIN /)\")\"", script, StringComparison.Ordinal);
        Assert.Contains("Expected the verified ext4 root filesystem", script, StringComparison.Ordinal);
        Assert.Contains("Expected exactly one LVM logical volume", script, StringComparison.Ordinal);
        Assert.Contains("must contain exactly one physical volume", script, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_PV:-/dev/sda3", script, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_DISK_BYTES:-136365211648", script, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_PARTITION_BYTES:-133088411648", script, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_INITIAL_LV_BYTES:-66542632960", script, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_INITIAL_FS_BYTES:-65174941696", script, StringComparison.Ordinal);
        Assert.Contains("state=\"filesystem-recovery\"", script, StringComparison.Ordinal);
        Assert.Contains("state=\"filesystem-expanded\"", script, StringComparison.Ordinal);
        Assert.Contains("lvextend --extents '+100%FREE'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("lvextend --resizefs", script, StringComparison.Ordinal);
        Assert.Contains("resize2fs \"$lv_path\"", script, StringComparison.Ordinal);
        Assert.Contains("minimum_expanded_fs_bytes", script, StringComparison.Ordinal);
    }

    private static IDictionary<object, object> ReadWorkflow()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "linux-runner-root-capacity.yml");
        return new DeserializerBuilder().Build().Deserialize<IDictionary<object, object>>(File.ReadAllText(workflowPath));
    }

    private static IDictionary<object, object> Mapping(object value)
        => Assert.IsAssignableFrom<IDictionary<object, object>>(value);

    private static IReadOnlyList<KeyValuePair<string, string>> ReadMatrixRunners(IDictionary<object, object> job)
    {
        var strategy = Mapping(job["strategy"]);
        var matrix = Mapping(strategy["matrix"]);
        var runners = Assert.IsAssignableFrom<IEnumerable<object>>(matrix["runner"]);
        return runners.Select(value =>
        {
            var runner = Mapping(value);
            return new KeyValuePair<string, string>(
                Assert.IsType<string>(runner["name"]),
                Assert.IsType<string>(runner["labels"]));
        }).ToArray();
    }

    private static void AssertPinnedCheckout(IDictionary<object, object> job)
    {
        var steps = Assert.IsAssignableFrom<IEnumerable<object>>(job["steps"]);
        var checkout = steps.Select(Mapping).Single(step => Equals(step["name"], "Check out PSPublishModule"));
        Assert.Equal("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", checkout["uses"]);
    }

    private static void AssertCapacityStep(
        IDictionary<object, object> job,
        string stepName,
        string expectedApply,
        string? expectedConfirmation)
    {
        var steps = Assert.IsAssignableFrom<IEnumerable<object>>(job["steps"]);
        var step = steps.Select(Mapping).Single(candidate => Equals(candidate["name"], stepName));
        var environment = Mapping(step["env"]);
        Assert.Equal("bash", step["shell"]);
        Assert.Equal("bash ./.github/scripts/expand-linux-runner-root.sh", step["run"]);
        Assert.Equal(expectedApply, environment["APPLY"]);
        Assert.Equal("${{ matrix.runner.name }}", environment["EXPECTED_RUNNER_NAME"]);
        Assert.Equal("/dev/sda3", environment["EXPECTED_PV"]);
        Assert.Equal("136365211648", environment["EXPECTED_DISK_BYTES"]);
        Assert.Equal("133088411648", environment["EXPECTED_PARTITION_BYTES"]);
        Assert.Equal("66542632960", environment["EXPECTED_INITIAL_LV_BYTES"]);
        Assert.Equal("65174941696", environment["EXPECTED_INITIAL_FS_BYTES"]);
        if (expectedConfirmation is null)
            Assert.False(environment.ContainsKey("CONFIRMATION"));
        else
            Assert.Equal(expectedConfirmation, environment["CONFIRMATION"]);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PSPublishModule.sln")) &&
                Directory.Exists(Path.Combine(current.FullName, ".github")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the PSPublishModule repository root.");
    }
}
