using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class ExistingFilePathIdentityResolverNet472SmokeTests
{
    [Fact]
    public void WindowsDirectoryStatusIsAvailableOnNet472()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return;
        }

        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Net472SmokeTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var status = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(root.FullName);

            Assert.StartsWith("windows", status.Identity, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(status.ChangeToken));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
