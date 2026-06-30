using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleReceiptStoreTests
{
    [Fact]
    public void WriteReceipt_RoundTripsLongModulePath()
    {
        using var root = new TemporaryDirectory();
        var moduleName = "Company." + new string('A', 180);
        var moduleRoot = Path.Combine(root.Path, "Modules");
        var modulePath = Path.Combine(moduleRoot, moduleName, "1.0.0-preview0001");
        var result = new ManagedModuleInstallResult
        {
            Name = moduleName,
            Version = "1.0.0-preview0001",
            Status = ManagedModuleInstallStatus.Installed,
            RepositoryName = "Local",
            RepositorySource = root.Path,
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            FileCount = 1,
            ExtractedBytes = 42,
            Download = new ManagedModuleDownloadResult
            {
                Name = moduleName,
                Version = "1.0.0-preview0001",
                RepositoryName = "Local",
                Source = root.Path,
                PackagePath = Path.Combine(root.Path, "package.nupkg"),
                BytesWritten = 42,
                PackageSha256 = new string('a', 64)
            }
        };
        var store = new ManagedModuleReceiptStore();

        var receiptPath = store.WriteReceipt(new ManagedModuleRepository("Local", root.Path), result);
        var receipt = store.TryReadReceipt(modulePath);

        Assert.Equal(ManagedModuleReceiptStore.GetReceiptPath(modulePath), receiptPath);
        if (Path.DirectorySeparatorChar == '\\')
            Assert.True(receiptPath.Length > 260);
        Assert.True(File.Exists(receiptPath));
        Assert.NotNull(receipt);
        Assert.Equal(moduleName, receipt.Name);
        Assert.Equal("1.0.0-preview0001", receipt.Version);
        Assert.Equal(receiptPath, result.ReceiptPath);
    }
}
