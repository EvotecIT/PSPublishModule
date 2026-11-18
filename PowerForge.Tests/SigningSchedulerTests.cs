using PowerForge;

namespace PowerForge.Tests;

public sealed class SigningSchedulerTests
{
    [Fact]
    public void Run_FailureStopsNewItemsWhileItemsInFlightFinish()
    {
        // Item 1 fails only once item 2 is in flight. Item 2 then stays blocked until the
        // failure's finish callback runs, which the scheduler posts only after recording the
        // failure. Item 2 must still finish, and no later item may start.
        var timeout = TimeSpan.FromSeconds(30);
        using var item2Started = new ManualResetEventSlim();
        using var failureRecorded = new ManualResetEventSlim();
        var startedItems = new List<int>();
        var finishedItems = new Dictionary<int, SigningItemOutcome>();

        var result = SigningScheduler.Run(
            count: 8,
            parallelism: 2,
            index =>
            {
                switch (index)
                {
                    case 1:
                        if (!item2Started.Wait(timeout)) throw new TimeoutException("Item 2 never started.");
                        throw new InvalidOperationException("Signing denied");
                    case 2:
                        item2Started.Set();
                        if (!failureRecorded.Wait(timeout)) throw new TimeoutException("The failure was never reported.");
                        return SigningItemOutcome.Signed;
                    default:
                        return SigningItemOutcome.Signed;
                }
            },
            started: startedItems.Add,
            finished: (index, outcome, _) =>
            {
                finishedItems[index] = outcome;
                if (outcome == SigningItemOutcome.Failed) failureRecorded.Set();
            });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(1, failure.Index);
        Assert.Equal("Signing denied", failure.Error?.Message);
        Assert.Equal(SigningItemOutcome.Signed, finishedItems[2]);
        Assert.Equal(new[] { 0, 1, 2 }, startedItems.OrderBy(index => index));
        Assert.All(Enumerable.Range(3, 5), index => Assert.False(result.Attempted[index]));
    }
}
