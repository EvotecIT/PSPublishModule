using PowerForge;

namespace PowerForge.Tests;

public sealed class BufferedLogSupportServiceTests
{
    [Fact]
    public void WriteTail_replays_last_entries_in_order()
    {
        var logger = new BufferedLogger { IsVerbose = true };
        var entries = new[]
        {
            new BufferedLogEntry("info", "one"),
            new BufferedLogEntry("warn", "two"),
            new BufferedLogEntry("error", "three")
        };

        new BufferedLogSupportService().WriteTail(entries, logger, maxEntries: 2);

        Assert.Collection(
            logger.Entries,
            entry =>
            {
                Assert.Equal("warn", entry.Level);
                Assert.Equal("Last 2/3 log lines:", entry.Message);
            },
            entry =>
            {
                Assert.Equal("warn", entry.Level);
                Assert.Equal("two", entry.Message);
            },
            entry =>
            {
                Assert.Equal("error", entry.Level);
                Assert.Equal("three", entry.Message);
            });
    }

    [Theory]
    [InlineData(150, "150ms")]
    [InlineData(1250, "1s 250ms")]
    [InlineData(61_000, "1m 1s")]
    [InlineData(3_661_000, "1h 1m 1s")]
    public void FormatDuration_returns_human_readable_output(int milliseconds, string expected)
    {
        var actual = new BufferedLogSupportService().FormatDuration(TimeSpan.FromMilliseconds(milliseconds));

        Assert.Equal(expected, actual);
    }
}
