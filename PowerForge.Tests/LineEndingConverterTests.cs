using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class LineEndingConverterTests
{
    [Fact]
    public void Convert_NormalizesLegacyCarriageReturnOnlyFiles_ToCrLf()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "Test.ps1");
            File.WriteAllText(file, "line1\rline2\r");

            var converter = new LineEndingConverter();
            var options = new LineEndingConversionOptions(
                enumeration: new ProjectEnumeration(
                    rootPath: root,
                    kind: ProjectKind.PowerShell,
                    customExtensions: null,
                    excludeDirectories: Array.Empty<string>()),
                target: LineEnding.CRLF,
                createBackups: false,
                backupDirectory: null,
                force: true,
                onlyMixed: false,
                ensureFinalNewline: false,
                onlyMissingNewline: false,
                preferUtf8BomForPowerShell: true);

            var result = converter.Convert(options);
            var text = File.ReadAllText(file);

            Assert.Equal(1, result.Converted);
            Assert.Equal("line1\r\nline2\r\n", text);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
