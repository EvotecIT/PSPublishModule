using System.Text;

namespace PowerForge.Tests;

public sealed class RedirectedProcessOutputTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public async Task Split_bom_and_multibyte_characters_are_decoded_without_corruption(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        const string expected = "zażółć😀\r\nbłąd漢字";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray();
        using var stream = new SingleByteStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();

        var output = RedirectedProcessOutput.Start(reader, lineReceived: lines.Add);
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected, output.Snapshot());
        Assert.Equal(new[] { "zażółć😀", "błąd漢字" }, lines);
    }

    private sealed class SingleByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken);
    }
}
