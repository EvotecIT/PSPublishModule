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
            var store = CreateResultStore(derivedData);

            var first = AppleBuiltAppResultStore.Preserve(snapshot, store);
            var second = AppleBuiltAppResultStore.Preserve(snapshot, store);

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
            var store = CreateResultStore(derivedData);
            var retained = AppleBuiltAppResultStore.Preserve(snapshot, store);
            File.WriteAllText(Path.Combine(retained, payload), "replacement");

            var error = Assert.Throws<InvalidOperationException>(() =>
                AppleBuiltAppResultStore.Preserve(snapshot, store));

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
            var store = CreateResultStore(derivedData);

            var first = AppleBuiltAppResultStore.Preserve(firstSnapshot, store);
            var second = AppleBuiltAppResultStore.Preserve(secondSnapshot, store);

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

    [Fact]
    public void Preserve_rejects_derived_data_replaced_by_a_symlink()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = CreateAppFixture(out _);
        var derivedData = Path.Combine(root.Parent!.FullName, "DerivedData");
        var displacedDerivedData = Path.Combine(
            root.Parent.FullName,
            "DerivedData.original");
        var redirected = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.BuiltAppResultRedirect",
            Guid.NewGuid().ToString("N")));
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);
            var store = CreateResultStore(derivedData);
            Directory.Move(derivedData, displacedDerivedData);
            Directory.CreateSymbolicLink(derivedData, redirected.FullName);

            var error = Assert.Throws<InvalidOperationException>(() =>
                AppleBuiltAppResultStore.Preserve(snapshot, store));

            Assert.Contains(
                "DerivedDataPath changed",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(redirected.EnumerateFileSystemInfos());
        }
        finally
        {
            try
            {
                if (Directory.Exists(derivedData) || File.Exists(derivedData))
                    Directory.Delete(derivedData);
            }
            catch { /* best effort */ }
            try { root.Parent!.Delete(recursive: true); } catch { /* best effort */ }
            try { redirected.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Preserve_rejects_an_existing_symlink_below_derived_data_before_writing()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = CreateAppFixture(out _);
        var derivedData = Path.Combine(root.Parent!.FullName, "DerivedData");
        var redirected = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.BuiltAppResultRedirect",
            Guid.NewGuid().ToString("N")));
        try
        {
            using var snapshot = AppleBuiltAppSnapshot.Create(root.FullName);
            var store = CreateResultStore(derivedData);
            Directory.CreateSymbolicLink(
                Path.Combine(derivedData, "PowerForge"),
                redirected.FullName);

            var error = Assert.Throws<InvalidOperationException>(() =>
                AppleBuiltAppResultStore.Preserve(snapshot, store));

            Assert.Contains(
                "must not traverse a symbolic link",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(redirected.EnumerateFileSystemInfos());
        }
        finally
        {
            try { root.Parent!.Delete(recursive: true); } catch { /* best effort */ }
            try { redirected.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static AppleStableDirectoryIdentity CreateResultStore(string path)
    {
        Directory.CreateDirectory(path);
        return AppleStableDirectoryIdentity.Capture(path, "DerivedDataPath");
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
