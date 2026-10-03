using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

[Trait("Category", "DotNetPublishPrGate")]
public sealed class RedirectedProcessOutputTests
{
    [Theory]
    [InlineData("closed")]
    [InlineData("inherited")]
    public async Task Unix_pipe_output_survives_worker_pool_pressure_without_losing_the_exit_boundary(string mode)
    {
        if (OperatingSystem.IsWindows()) return;
        var fixture = Path.Combine(AppContext.BaseDirectory, "ProcessOutputPressureFixture", "PowerForge.ProcessOutputPressureFixture.dll");
        Assert.True(File.Exists(fixture), fixture);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add(mode);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(process.ExitCode == 0, await error);
            using var result = JsonDocument.Parse(await output);
            Assert.Equal(0, result.RootElement.GetProperty("ExitCode").GetInt32());
            Assert.False(result.RootElement.GetProperty("TimedOut").GetBoolean());
            Assert.True(result.RootElement.GetProperty("CompletedWhilePoolBlocked").GetBoolean());
            Assert.EndsWith("parent-output😀", result.RootElement.GetProperty("StdOut").GetString());
            Assert.Equal("parent-error漢字", result.RootElement.GetProperty("StdErr").GetString());
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

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
