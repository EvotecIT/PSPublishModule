using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleBuiltAppSnapshotTests
{
    [Fact]
    public void ValidateUnchanged_rejects_a_transient_file_write_restored_before_validation()
    {
        var root = CreateAppFixture(out var payload);
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);

            File.WriteAllText(Path.Combine(snapshot.AppPath, payload), "replacement");
            File.WriteAllText(Path.Combine(snapshot.AppPath, payload), "approved");

            var error = Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
            Assert.Contains("private built Apple app snapshot changed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateUnchanged_rejects_a_transient_created_file_removed_before_validation()
    {
        var root = CreateAppFixture(out _);
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);
            var transient = Path.Combine(snapshot.AppPath, "transient");

            File.WriteAllText(transient, "temporary");
            File.Delete(transient);

            var error = Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
            Assert.Contains("private built Apple app snapshot changed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static DirectoryInfo CreateAppFixture(out string payload)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.BuiltAppSnapshot",
            Guid.NewGuid().ToString("N"),
            "CasaRay.app"));
        payload = "payload";
        File.WriteAllText(Path.Combine(root.FullName, payload), "approved");
        return root;
    }
}
