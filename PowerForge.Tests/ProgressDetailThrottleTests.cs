using System;
using System.Collections.Generic;

namespace PowerForge.Tests;

public sealed class ProgressDetailThrottleTests
{
    [Fact]
    public void ChattyProcessOutput_ReachesTheStepAsOneBoundedDetailPerInterval()
    {
        var details = new List<string>();
        var sink = ProgressDetailThrottle.Create(details.Add, TimeSpan.FromMinutes(1));

        sink("   ");
        sink("  Restored C:\\repo\\src\\Project.csproj (in 1.2 sec).\r\nsecond line  ");
        for (var i = 0; i < 1000; i++)
            sink($"line {i}");

        var detail = Assert.Single(details);
        Assert.Equal("Restored C:\\repo\\src\\Project.csproj (in 1.2 sec).", detail);
    }
}
