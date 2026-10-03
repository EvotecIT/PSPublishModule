using System.Threading.Channels;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerWatchRecoveryTests
{
    [Fact]
    public async Task WatchPipeline_RecoversAfterIncompleteConfigurationSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-watch-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        var pipeline = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipeline, "{ incomplete json");
        File.WriteAllText(Path.Combine(root, "site.json"), """{"collections":[{"name":"pages","input":"content","output":"/"}]}""");
        File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nRecovered content");
        var runs = Channel.CreateUnbounded<WebPipelineResult>();
        using var cancellation = new CancellationTokenSource();
        var watcher = Task.Run(() => WebPipelineRunner.WatchPipelineLoop(pipeline, new WebConsoleLogger(),
            cancellation.Token, false, false, null, null, null, result => runs.Writer.TryWrite(result)));
        try
        {
            var failed = await runs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(failed.Success);
            Assert.False(watcher.IsCompleted);
            File.WriteAllText(pipeline, """{"steps":[{"task":"build","config":"site.json","out":"_site"}]}""");
            WebPipelineResult recovered;
            do
            {
                recovered = await runs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            } while (!recovered.Success);
            Assert.Contains("Recovered content", File.ReadAllText(Path.Combine(root, "_site", "index.html")));
            Assert.False(watcher.IsCompleted);
        }
        finally
        {
            cancellation.Cancel();
            await watcher.WaitAsync(TimeSpan.FromSeconds(15));
            Directory.Delete(root, recursive: true);
        }
    }
}
