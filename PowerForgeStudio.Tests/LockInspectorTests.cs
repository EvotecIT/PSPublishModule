using PowerForge;

namespace PowerForgeStudio.Tests;

public sealed class LockInspectorTests
{
    [Fact]
    public void RestartManagerDistinguishesSuccessfulEmptyEvidenceFromApiFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-lock-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "held.txt");
        File.WriteAllText(path, "held");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var succeeded = LockInspector.TryGetLockingProcesses([path], out var processes, out var errorCode);

            Assert.True(succeeded, $"Restart Manager failed with error {errorCode}.");
            Assert.Contains(processes, process => process.Pid == Environment.ProcessId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
