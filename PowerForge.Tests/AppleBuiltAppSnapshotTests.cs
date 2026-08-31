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

    [Fact]
    public void Preserve_reuses_an_identical_content_addressed_result_product()
    {
        var root = CreateAppFixture(out var payload);
        var derivedData = Path.Combine(
            root.Parent!.FullName,
            "DerivedData");
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);

            var first = AppleBuiltAppResultStore.Preserve(snapshot, derivedData);
            var second = AppleBuiltAppResultStore.Preserve(snapshot, derivedData);

            Assert.Equal(first, second);
            Assert.Equal("approved", File.ReadAllText(Path.Combine(second, payload)));
        }
        finally
        {
            try { root.Parent!.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Preserve_rejects_a_modified_content_addressed_result_product()
    {
        var root = CreateAppFixture(out var payload);
        var derivedData = Path.Combine(
            root.Parent!.FullName,
            "DerivedData");
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);
            var retained = AppleBuiltAppResultStore.Preserve(snapshot, derivedData);
            File.WriteAllText(Path.Combine(retained, payload), "replacement");

            var error = Assert.Throws<InvalidOperationException>(() =>
                AppleBuiltAppResultStore.Preserve(snapshot, derivedData));

            Assert.Contains(
                "does not match the provenance-bound build",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Parent!.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Preserve_namespaces_identical_products_by_bundle_name()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.BuiltAppSnapshot",
            Guid.NewGuid().ToString("N")));
        var firstApp = Directory.CreateDirectory(Path.Combine(root.FullName, "First.app"));
        var secondApp = Directory.CreateDirectory(Path.Combine(root.FullName, "Second.app"));
        File.WriteAllText(Path.Combine(firstApp.FullName, "payload"), "identical");
        File.WriteAllText(Path.Combine(secondApp.FullName, "payload"), "identical");
        var derivedData = Path.Combine(root.FullName, "DerivedData");
        try
        {
            using var firstSnapshot = AppleBuiltAppSnapshot.Create(firstApp.FullName);
            using var secondSnapshot = AppleBuiltAppSnapshot.Create(secondApp.FullName);

            var first = AppleBuiltAppResultStore.Preserve(firstSnapshot, derivedData);
            var second = AppleBuiltAppResultStore.Preserve(secondSnapshot, derivedData);

            Assert.NotEqual(first, second);
            Assert.Equal("First.app", Path.GetFileName(first));
            Assert.Equal("Second.app", Path.GetFileName(second));
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
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
