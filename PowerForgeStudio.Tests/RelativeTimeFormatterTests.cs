using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Tests;

public sealed class RelativeTimeFormatterTests
{
    [Fact]
    public void Format_LessThanOneMinute_ReturnsJustNow()
    {
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(-30);
        Assert.Equal("just now", RelativeTimeFormatter.Format(timestamp));
    }

    [Fact]
    public void Format_FiveMinutesAgo_Returns5m()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.Equal("5m", RelativeTimeFormatter.Format(timestamp));
    }

    [Fact]
    public void Format_ThreeHoursAgo_Returns3h()
    {
        var timestamp = DateTimeOffset.UtcNow.AddHours(-3);
        Assert.Equal("3h", RelativeTimeFormatter.Format(timestamp));
    }

    [Fact]
    public void Format_TwoDaysAgo_Returns2d()
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-2);
        Assert.Equal("2d", RelativeTimeFormatter.Format(timestamp));
    }

    [Fact]
    public void FormatWithAgo_FiveMinutesAgo_Returns5mAgo()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.Equal("5m ago", RelativeTimeFormatter.FormatWithAgo(timestamp));
    }
}
