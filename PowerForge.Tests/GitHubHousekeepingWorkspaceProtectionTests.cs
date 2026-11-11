namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class GitHubHousekeepingWorkspaceProtectionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Run_ProtectedWorkspaceForCurrentMachine_PreservesDataAndCleansOtherWorkspace(bool dryRun, bool machineLocal)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.WorkspaceProtection", Guid.NewGuid().ToString("N"));
        string work = Path.Combine(root, "_work");
        string temp = Directory.CreateDirectory(Path.Combine(work, "_temp")).FullName;
        string retained = Directory.CreateDirectory(Path.Combine(work, "retained-data")).FullName;
        string disposable = Directory.CreateDirectory(Path.Combine(work, "completed-job")).FullName;
        string retainedFile = Path.Combine(retained, "data.sqlite");
        string disposableFile = Path.Combine(disposable, "output.txt");
        File.WriteAllText(retainedFile, "retained fixture data");
        File.WriteAllText(disposableFile, "completed job output");

        const string variable = "POWERFORGE_RUNNER_PROTECTED_WORKSPACES";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, machineLocal ? "[\"retained-data\"]" : null);
            var result = new GitHubHousekeepingService(new NullLogger()).Run(new GitHubHousekeepingSpec
            {
                DryRun = dryRun,
                Artifacts = new GitHubHousekeepingArtifactSpec { Enabled = false },
                Caches = new GitHubHousekeepingCacheSpec { Enabled = false },
                Runner = new GitHubHousekeepingRunnerSpec
                {
                    Enabled = true,
                    RunnerRootPath = root,
                    WorkRootPath = work,
                    RunnerTempPath = temp,
                    MinFreeGb = null,
                    CleanWorkspaces = true,
                    WorkspacesRetentionDays = 0,
                    CleanDiagnostics = false,
                    CleanRunnerTemp = false,
                    CleanActionsCache = false,
                    CleanToolCache = false,
                    ClearDotNetCaches = false,
                    PruneDocker = false,
                    ProtectedWorkspaces = new Dictionary<string, string[]>
                    {
                        [Environment.MachineName.ToLowerInvariant()] = machineLocal ? Array.Empty<string>() : new[] { "retained-data" },
                        ["another-machine-" + Guid.NewGuid().ToString("N")] = new[] { "completed-job" }
                    }
                }
            });

            Assert.True(result.Success, result.Message);
            Assert.Equal("retained fixture data", File.ReadAllText(retainedFile));
            var step = Assert.Single(result.Runner!.Steps);
            Assert.Equal("workspaces", step.Id);
            Assert.Equal(new[] { disposable }, step.Targets);
            Assert.Equal(dryRun, File.Exists(disposableFile));
            Assert.True(Directory.Exists(temp));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            Directory.Delete(root, recursive: true);
        }
    }
}
