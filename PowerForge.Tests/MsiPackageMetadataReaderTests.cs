using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class MsiPackageMetadataReaderTests
{
    [Fact]
    public void ReadProperties_ThrowsWhenMsiFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"), "missing.msi");

        var ex = Assert.Throws<FileNotFoundException>(
            () => MsiPackageMetadataReader.ReadProperties(path, new[] { "ProductCode" }));

        Assert.Equal(path, ex.FileName);
    }
}
