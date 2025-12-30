using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class EncodingConverterTests
{
    [Fact]
    public void Convert_SourceAny_SkipsFilesAlreadyInTargetEncoding()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var bomPath = Path.Combine(root.FullName, "bom.ps1");
            var noBomPath = Path.Combine(root.FullName, "nobom.ps1");

            File.WriteAllText(bomPath, "hello", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(noBomPath, "hello", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var enumeration = new ProjectEnumeration(
                rootPath: root.FullName,
                kind: ProjectKind.PowerShell,
                customExtensions: null,
                excludeDirectories: Array.Empty<string>());

            var opts = new EncodingConversionOptions(
                enumeration: enumeration,
                sourceEncoding: TextEncodingKind.Any,
                targetEncoding: TextEncodingKind.UTF8BOM,
                createBackups: false,
                backupDirectory: null,
                force: false,
                noRollbackOnMismatch: false,
                preferUtf8BomForPowerShell: true);

            var converter = new EncodingConverter();
            var res = converter.Convert(opts);

            Assert.Equal(2, res.Total);
            Assert.Equal(1, res.Converted);
            Assert.Equal(1, res.Skipped);
            Assert.Equal(0, res.Errors);

            var bom = res.Files.Single(f => string.Equals(f.Path, bomPath, StringComparison.OrdinalIgnoreCase));
            var noBom = res.Files.Single(f => string.Equals(f.Path, noBomPath, StringComparison.OrdinalIgnoreCase));

            Assert.Equal("Skipped", bom.Status);
            Assert.Equal("Converted", noBom.Status);

            Assert.True(File.ReadAllBytes(bomPath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.True(File.ReadAllBytes(noBomPath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

