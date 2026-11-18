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

    [Fact]
    public void ProcessThatGoesQuiet_StillShowsItsLastLine()
    {
        var details = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var sink = ProgressDetailThrottle.Create(details.Enqueue, TimeSpan.FromMilliseconds(50));

        sink("Restoring packages");
        sink("Signing PowerForge.1.0.0.nupkg");
        sink("Waiting for the certificate PIN");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (details.Count < 2 && DateTime.UtcNow < deadline)
            System.Threading.Thread.Sleep(10);

        Assert.Equal(new[] { "Restoring packages", "Waiting for the certificate PIN" }, details.ToArray());
    }
}
