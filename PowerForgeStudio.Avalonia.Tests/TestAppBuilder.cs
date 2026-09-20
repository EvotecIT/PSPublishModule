using Avalonia;
using Avalonia.Headless;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PowerForgeStudio.Avalonia.Tests;

internal static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder), AvaloniaTestIsolationLevel.PerTest);
        try { return await session.Dispatch(action, CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            // Never synchronously join the headless dispatcher from its own thread.
            await Task.Run(session.Dispose).ConfigureAwait(false);
        }
    }
}
