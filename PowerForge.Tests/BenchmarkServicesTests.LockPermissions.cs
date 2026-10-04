using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidenceCatalog_ExistingUnixLockPermissionsRemainProtected()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = CreateTempRoot();
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute);
            string destination = Path.Combine(root, "index.json");
            string lockPath = BenchmarkFileUpdateLock.CreateLockPath(destination);
            File.WriteAllText(lockPath, "existing");
            const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(lockPath, privateMode);
            using (BenchmarkFileUpdateLock.Acquire(destination)) { }
            Assert.Equal(privateMode, File.GetUnixFileMode(lockPath));
            Assert.Equal("existing", File.ReadAllText(lockPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
