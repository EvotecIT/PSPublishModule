using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void MoveDirectoryWithRetries_PromotesPayloadAfterTransientWindowsFailures()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var sourcePath = Path.Combine(root.FullName, "payload.tmp");
            var destinationPath = Path.Combine(root.FullName, "payload");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "signed.bin"), "signed-payload");

            var attempts = 0;
            var delays = new List<int>();
            ModulePipelineRunner.MoveDirectoryWithRetries(
                sourcePath,
                destinationPath,
                "Payload promotion failed.",
                moveDirectory: (source, destination) =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new UnauthorizedAccessException("Simulated scanner lock.");
                    if (attempts == 2)
                        throw new IOException("Simulated indexing lock.");
                    Directory.Move(source, destination);
                },
                delay: delays.Add);

            Assert.Equal(3, attempts);
            Assert.Equal(new[] { 50, 100 }, delays);
            Assert.False(Directory.Exists(sourcePath));
            Assert.Equal("signed-payload", File.ReadAllText(Path.Combine(destinationPath, "signed.bin")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void MoveDirectoryWithRetries_ExplainsSafeRetryWhenPromotionRemainsLocked()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "payload.tmp");
        var destinationPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "payload");
        var attempts = 0;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ModulePipelineRunner.MoveDirectoryWithRetries(
                sourcePath,
                destinationPath,
                "No remote publish attempt began; retry the checkpoint safely.",
                moveDirectory: (_, _) =>
                {
                    attempts++;
                    throw new UnauthorizedAccessException("Persistent scanner lock.");
                },
                delay: _ => { },
                maximumAttempts: 3,
                initialDelayMs: 1));

        Assert.Equal(3, attempts);
        Assert.Contains("No remote publish attempt began", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Failed after 3 attempts", exception.Message, StringComparison.Ordinal);
        Assert.Contains(sourcePath, exception.Message, StringComparison.Ordinal);
        Assert.Contains(destinationPath, exception.Message, StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    [Fact]
    public void RunFileSystemOperationWithRetries_RestoresFileAfterTransientLock()
    {
        var attempts = 0;
        var delays = new List<int>();

        ModulePipelineRunner.RunFileSystemOperationWithRetries(
            operation: () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new UnauthorizedAccessException("Simulated scanner lock.");
            },
            failureDescription: "Payload restoration failed.",
            operationDescription: "moving a cached package file",
            delay: delays.Add);

        Assert.Equal(3, attempts);
        Assert.Equal(new[] { 50, 100 }, delays);
    }

    [Fact]
    public void CleanupTemporaryPath_PreservesPrimarySafeResumeFailure()
    {
        var primary = new InvalidOperationException(
            "No remote publish attempt began; retry the checkpoint safely.");
        var cleanup = new UnauthorizedAccessException("Persistent scanner lock.");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ModulePipelineRunner.CleanupTemporaryPath(
                "payload.tmp",
                primary,
                () => throw cleanup));

        Assert.Contains(primary.Message, exception.Message, StringComparison.Ordinal);
        Assert.Contains("retained temporary path", exception.Message, StringComparison.Ordinal);
        var aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Same(primary, aggregate.InnerExceptions[0]);
        Assert.Same(cleanup, aggregate.InnerExceptions[1]);
    }
}
