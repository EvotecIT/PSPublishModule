using PowerForge;

namespace PowerForge.Tests;

public sealed class SigningSchedulerTests
{
    [Fact]
    public void Run_FailureStopsNewItemsWhileItemsInFlightFinish()
    {
        // Item 1 fails. Item 2, if a worker claimed it, stays blocked until the failure's
        // finish callback runs, which the scheduler posts only after recording the failure.
        // No item after that may start, whatever the worker timing.
        using var failureRecorded = new ManualResetEventSlim();
        var startedItems = new List<int>();

        var result = SigningScheduler.Run(
            count: 8,
            parallelism: 2,
            index => index switch
            {
                0 => SigningItemOutcome.Signed,
                1 => throw new InvalidOperationException("Signing denied"),
                2 => failureRecorded.Wait(TimeSpan.FromSeconds(30))
                    ? SigningItemOutcome.Signed
                    : throw new TimeoutException("The failure was never reported."),
                _ => SigningItemOutcome.Signed
            },
            started: startedItems.Add,
            finished: (index, outcome, _) =>
            {
                if (outcome == SigningItemOutcome.Failed) failureRecorded.Set();
            });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(1, failure.Index);
        Assert.Equal("Signing denied", failure.Error?.Message);
        Assert.All(startedItems, index => Assert.InRange(index, 0, 2));
        Assert.All(Enumerable.Range(3, 5), index => Assert.False(result.Attempted[index]));
    }
}
