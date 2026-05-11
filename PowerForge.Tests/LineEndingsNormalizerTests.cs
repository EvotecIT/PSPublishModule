using System.Text;

namespace PowerForge.Tests;

public sealed class LineEndingsNormalizerTests
{
    [Fact]
    public void NormalizeFile_ToCrLf_ConvertsBareCrAndLf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-lineendings-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(path, "one\ntwo\rthree\r\nfour", new UTF8Encoding(false));

            var result = new LineEndingsNormalizer().NormalizeFile(
                path,
                new NormalizationOptions(LineEnding.CRLF, ensureUtf8Bom: false));

            var text = File.ReadAllText(path, new UTF8Encoding(false));

            Assert.True(result.Changed);
            Assert.Equal("one\r\ntwo\r\nthree\r\nfour", text);
            Assert.DoesNotContain("\rt", text);
            Assert.DoesNotContain("e\nf", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeFile_ToLf_ConvertsBareCrAndCrLf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-lineendings-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(path, "one\r\ntwo\rthree", new UTF8Encoding(false));

            var result = new LineEndingsNormalizer().NormalizeFile(
                path,
                new NormalizationOptions(LineEnding.LF, ensureUtf8Bom: false));

            var text = File.ReadAllText(path, new UTF8Encoding(false));

            Assert.True(result.Changed);
            Assert.Equal("one\ntwo\nthree", text);
            Assert.DoesNotContain('\r', text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
