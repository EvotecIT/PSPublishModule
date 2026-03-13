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
                DryRun = true,
                Aggressive = false,
                ClearDotNetCaches = false,
                PruneDocker = false
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
