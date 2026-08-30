using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleBuildProvenanceTests
{
    [Fact]
    public void ResolveLocalSourceRevision_distinguishes_clean_and_dirty_builds()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N")));
        try
        {
            RunGit(root.FullName, "init");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), "clean");
            RunGit(root.FullName, "add", "tracked.txt");
            RunGit(root.FullName, "commit", "-m", "fixture");
            var head = RunGit(root.FullName, "rev-parse", "HEAD").Trim().ToLowerInvariant();

            Assert.Equal(head, AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));

            File.WriteAllText(Path.Combine(root.FullName, "untracked.txt"), "dirty");
            Assert.Equal(head + "-dirty", AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output;
    }
}
