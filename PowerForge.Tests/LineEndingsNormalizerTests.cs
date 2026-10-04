using System.Text;

namespace PowerForge.Tests;

public sealed class LineEndingsNormalizerTests
{
    [Theory]
    [InlineData(LineEnding.CRLF, true)]
    [InlineData(LineEnding.CRLF, false)]
    [InlineData(LineEnding.LF, true)]
    [InlineData(LineEnding.LF, false)]
    public void NormalizeFile_AlreadyNormalized_PreservesBytesAndLastWriteTime(LineEnding ending, bool bom)
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-lineendings-{Guid.NewGuid():N}.ps1");
        try
        {
            var newline = ending == LineEnding.CRLF ? "\r\n" : "\n";
            File.WriteAllText(path, "München" + newline + "Δ", new UTF8Encoding(bom));
            File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var originalTime = File.GetLastWriteTimeUtc(path);
            var originalBytes = File.ReadAllBytes(path);

            var result = new LineEndingsNormalizer().NormalizeFile(path, new NormalizationOptions(ending, bom));

            Assert.False(result.Changed);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal(originalTime, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NormalizeFile_ChangesBom_ReportsChanged(bool inputBom, bool outputBom)
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-lineendings-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(path, "München\r\nΔ", new UTF8Encoding(inputBom));

            var result = new LineEndingsNormalizer().NormalizeFile(path, new NormalizationOptions(LineEnding.CRLF, outputBom));

            Assert.True(result.Changed);
            var encoding = new UTF8Encoding(outputBom);
            Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes("München\r\nΔ")), File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeFile_Utf16_ConvertsToRequestedUtf8AndReportsChanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-lineendings-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(path, "München\r\nΔ", Encoding.Unicode);

            var result = new LineEndingsNormalizer().NormalizeFile(path, new NormalizationOptions(LineEnding.CRLF, true));

            Assert.True(result.Changed);
            var encoding = new UTF8Encoding(true);
            Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes("München\r\nΔ")), File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

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
