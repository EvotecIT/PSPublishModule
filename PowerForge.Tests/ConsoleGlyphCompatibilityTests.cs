
namespace PowerForge.Tests;

public sealed class ConsoleGlyphCompatibilityTests
{
    [Theory]
    [InlineData(true, true, 10, 14393, null, false)]
    [InlineData(true, true, 6, 9600, null, false)]
    [InlineData(true, true, 10, 20348, null, true)]
    [InlineData(true, false, 6, 0, null, true)]
    [InlineData(true, true, 10, 14393, "rich", true)]
    [InlineData(true, true, 10, 20348, "unicode", false)]
    [InlineData(true, true, 10, 20348, "ASCII", false)]
    [InlineData(false, true, 10, 20348, "rich", false)]
    public void EmojiSelection_DistinguishesEncodingFromFontCompatibility(
        bool unicode, bool windows, int major, int build, string? mode, bool expected)
        => Assert.Equal(expected, ConsoleEncoding.SelectEmojiSupport(
            unicode, windows, new Version(major, 0, build), mode));
}
