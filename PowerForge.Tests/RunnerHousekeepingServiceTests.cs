namespace PowerForge.Tests;

public sealed class RunnerHousekeepingServiceTests
{
    [Fact]
    public void Clean_DryRun_PlansDiagnosticsAndRunnerTempCleanup()
    {
        var root = CreateSandbox();
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var diagRoot = Path.Combine(runnerRoot, "_diag");
            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(diagRoot);

            var tempFile = Path.Combine(runnerTemp, "temp.txt");
            File.WriteAllText(tempFile, "temp");

            var oldDiag = Path.Combine(diagRoot, "old.log");
            File.WriteAllText(oldDiag, "old");
            File.SetLastWriteTimeUtc(oldDiag, DateTime.UtcNow.AddDays(-20));

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                DiagnosticsRootPath = diagRoot,
                MinFreeGb = null,
                DryRun = true,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanWorkspaces = false
            });

            Assert.True(result.Success);
            Assert.True(result.DryRun);
            Assert.Equal(2, result.Steps.Length);
            Assert.Contains(result.Steps, s => s.Id == "diag" && s.DryRun && s.EntriesAffected == 1);
            Assert.Contains(result.Steps, s => s.Id == "runner-temp" && s.DryRun && s.EntriesAffected == 1);
            Assert.True(File.Exists(oldDiag));
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_AggressiveModeDeletesOldDirectories()
    {
        var root = CreateSandbox();
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var actionsRoot = Path.Combine(workRoot, "_actions");
            var toolCache = Path.Combine(root, "toolcache");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(actionsRoot);
            Directory.CreateDirectory(toolCache);

            var oldActionDir = Path.Combine(actionsRoot, "old-action");
            var oldToolDir = Path.Combine(toolCache, "old-tool");
            Directory.CreateDirectory(oldActionDir);
            Directory.CreateDirectory(oldToolDir);

            File.WriteAllText(Path.Combine(oldActionDir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(oldToolDir, "b.txt"), "x");
            Directory.SetLastWriteTimeUtc(oldActionDir, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(oldToolDir, DateTime.UtcNow.AddDays(-40));

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                ToolCachePath = toolCache,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = true,
                ClearDotNetCaches = false,
                PruneDocker = false
            });

            Assert.True(result.Success);
            Assert.True(result.AggressiveApplied);
            Assert.DoesNotContain(result.Steps, s => !s.Success);
            Assert.False(Directory.Exists(oldActionDir));
            Assert.False(Directory.Exists(oldToolDir));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_RemovesOldRepositoryWorkspacesButKeepsCurrentAndInternalDirectories()
    {
        var root = CreateSandbox();
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var actionsRoot = Path.Combine(workRoot, "_actions");
            var oldWorkspace = Path.Combine(workRoot, "OldRepo");
            var currentWorkspaceRoot = Path.Combine(workRoot, "CurrentRepo");
            var currentWorkspace = Path.Combine(currentWorkspaceRoot, "CurrentRepo");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(actionsRoot);
            Directory.CreateDirectory(oldWorkspace);
            Directory.CreateDirectory(currentWorkspace);

            var oldWorkspaceFile = Path.Combine(oldWorkspace, "old.txt");
            File.WriteAllText(oldWorkspaceFile, "x");
            File.WriteAllText(Path.Combine(currentWorkspace, "current.txt"), "x");
            File.WriteAllText(Path.Combine(actionsRoot, "action.txt"), "x");
            Directory.SetLastWriteTimeUtc(oldWorkspace, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(oldWorkspaceFile, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(currentWorkspaceRoot, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(actionsRoot, DateTime.UtcNow.AddDays(-10));
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", currentWorkspace);

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanRunnerTemp = false,
                CleanWorkspaces = true
            });

            Assert.True(result.Success);
            Assert.Contains(result.Steps, s => s.Id == "workspaces" && s.EntriesAffected == 1);
            Assert.False(Directory.Exists(oldWorkspace));
            Assert.True(Directory.Exists(currentWorkspaceRoot));
            Assert.True(Directory.Exists(actionsRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_DeletesAgeQualifiedWorkspacesWhenGitHubWorkspaceIsUnset()
    {
        var root = CreateSandbox();
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var oldWorkspace = Path.Combine(workRoot, "OldRepo");
            var recentWorkspace = Path.Combine(workRoot, "RecentRepo");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(oldWorkspace);
            Directory.CreateDirectory(recentWorkspace);
            var oldWorkspaceFile = Path.Combine(oldWorkspace, "old.txt");
            File.WriteAllText(oldWorkspaceFile, "x");
            File.WriteAllText(Path.Combine(recentWorkspace, "recent.txt"), "x");
            Directory.SetLastWriteTimeUtc(oldWorkspace, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(oldWorkspaceFile, DateTime.UtcNow.AddDays(-10));
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", null);

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanRunnerTemp = false,
                CleanWorkspaces = true
            });

            Assert.True(result.Success);
            Assert.False(Directory.Exists(oldWorkspace));
            Assert.True(Directory.Exists(recentWorkspace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_UsesDescendantActivityForWorkspaceAge()
    {
        var root = CreateSandbox();
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var activeWorkspaceRoot = Path.Combine(workRoot, "ActiveRepo");
            var activeWorkspace = Path.Combine(activeWorkspaceRoot, "ActiveRepo");
            var staleParentFreshCheckout = Path.Combine(workRoot, "FreshRepo");
            var freshCheckout = Path.Combine(staleParentFreshCheckout, "FreshRepo");
            var staleParentStaleCheckout = Path.Combine(workRoot, "StaleRepo");
            var staleCheckout = Path.Combine(staleParentStaleCheckout, "StaleRepo");
            var staleParentFreshCustomCheckout = Path.Combine(workRoot, "CustomPathRepo");
            var customCheckout = Path.Combine(staleParentFreshCustomCheckout, "src", "main");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(activeWorkspace);
            Directory.CreateDirectory(freshCheckout);
            Directory.CreateDirectory(staleCheckout);
            Directory.CreateDirectory(customCheckout);
            var freshFile = Path.Combine(freshCheckout, "fresh.txt");
            var customFreshFile = Path.Combine(customCheckout, "fresh.txt");
            var staleFile = Path.Combine(staleCheckout, "stale.txt");
            File.WriteAllText(freshFile, "x");
            File.WriteAllText(customFreshFile, "x");
            File.WriteAllText(staleFile, "x");
            Directory.SetLastWriteTimeUtc(staleParentFreshCheckout, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(freshCheckout, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow);
            Directory.SetLastWriteTimeUtc(staleParentFreshCustomCheckout, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(Path.Combine(staleParentFreshCustomCheckout, "src"), DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(customCheckout, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(customFreshFile, DateTime.UtcNow);
            Directory.SetLastWriteTimeUtc(staleParentStaleCheckout, DateTime.UtcNow.AddDays(-10));
            Directory.SetLastWriteTimeUtc(staleCheckout, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-10));
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", activeWorkspace);

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanRunnerTemp = false,
                CleanWorkspaces = true
            });

            Assert.True(result.Success);
            Assert.True(Directory.Exists(staleParentFreshCheckout));
            Assert.True(Directory.Exists(staleParentFreshCustomCheckout));
            Assert.False(Directory.Exists(staleParentStaleCheckout));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            TryDelete(root);
        }
    }

    [Fact]
    public void DeleteTarget_MissingDirectoryIsTreatedAsAlreadyCleaned()
    {
        var service = new RunnerHousekeepingService(new NullLogger());
        var method = typeof(RunnerHousekeepingService).GetMethod("DeleteTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);

        var root = CreateSandbox();
        try
        {
            var missingTarget = Path.Combine(root, "already-gone");
            method!.Invoke(service, new object?[] { missingTarget, false, true, root });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PathsEqual_NormalizesDotDotSegments()
    {
        var method = typeof(RunnerHousekeepingService).GetMethod("PathsEqual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var root = CreateSandbox();
        try
        {
            var workRoot = Path.Combine(root, "_work");
            var target = Path.Combine(workRoot, "OldRepo");
            var equivalent = Path.Combine(workRoot, "..", "_work", "OldRepo");
            Directory.CreateDirectory(target);

            var result = method!.Invoke(null, new object?[] { target, equivalent });
            Assert.True((bool)result!);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_DoesNotProtectWorkspaceOutsideWorkRoot()
    {
        var root = CreateSandbox();
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var oldWorkspace = Path.Combine(workRoot, "OldRepo");
            var outsideWorkspace = Path.Combine(root, "outside", "Repo", "Repo");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(oldWorkspace);
            Directory.CreateDirectory(outsideWorkspace);
            var oldWorkspaceFile = Path.Combine(oldWorkspace, "old.txt");
            File.WriteAllText(oldWorkspaceFile, "x");
            Directory.SetLastWriteTimeUtc(oldWorkspace, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(oldWorkspaceFile, DateTime.UtcNow.AddDays(-10));
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", outsideWorkspace);

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanRunnerTemp = false,
                CleanWorkspaces = true
            });

            Assert.True(result.Success);
            Assert.False(Directory.Exists(oldWorkspace));
            Assert.True(Directory.Exists(outsideWorkspace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            TryDelete(root);
        }
    }

    [Fact]
    public void Clean_Apply_ZeroWorkspaceRetentionDeletesNonActiveWorkspaceImmediately()
    {
        var root = CreateSandbox();
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try
        {
            var runnerRoot = Path.Combine(root, "runner");
            var workRoot = Path.Combine(runnerRoot, "_work");
            var runnerTemp = Path.Combine(workRoot, "_temp");
            var activeWorkspaceRoot = Path.Combine(workRoot, "ActiveRepo");
            var activeWorkspace = Path.Combine(activeWorkspaceRoot, "ActiveRepo");
            var nonActiveWorkspace = Path.Combine(workRoot, "NonActiveRepo");

            Directory.CreateDirectory(runnerTemp);
            Directory.CreateDirectory(activeWorkspace);
            Directory.CreateDirectory(nonActiveWorkspace);
            File.WriteAllText(Path.Combine(activeWorkspace, "active.txt"), "x");
            File.WriteAllText(Path.Combine(nonActiveWorkspace, "non-active.txt"), "x");
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", activeWorkspace);

            var service = new RunnerHousekeepingService(new NullLogger());
            var result = service.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = runnerTemp,
                RunnerRootPath = runnerRoot,
                WorkRootPath = workRoot,
                WorkspacesRetentionDays = 0,
                MinFreeGb = null,
                DryRun = false,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false,
                CleanRunnerTemp = false,
                CleanWorkspaces = true
            });

            Assert.True(result.Success);
            Assert.True(Directory.Exists(activeWorkspaceRoot));
            Assert.False(Directory.Exists(nonActiveWorkspace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            TryDelete(root);
        }
    }

    [Fact]
    public void GuardedSudoDelete_RejectsTargetsOutsideAllowedRoot()
    {
        var service = new RunnerHousekeepingService(new NullLogger());
        var method = typeof(RunnerHousekeepingService).GetMethod("EnsureDeleteTargetWithinRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var root = CreateSandbox();
        try
        {
            var allowedRoot = Path.Combine(root, "allowed");
            var outsideTarget = Path.Combine(root, "outside", "temp");
            Directory.CreateDirectory(allowedRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(outsideTarget)!);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { outsideTarget, allowedRoot }));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("outside", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(0L, "0.0")]
    [InlineData(536_870_912L, "0.5")]
    [InlineData(1_610_612_736L, "1.5")]
    public void FormatGiB_PreservesFractionalValues(long bytes, string expected)
    {
        var method = typeof(RunnerHousekeepingService).GetMethod("FormatGiB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { bytes });
        Assert.Equal(expected, Assert.IsType<string>(value));
    }

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.RunnerHousekeepingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
