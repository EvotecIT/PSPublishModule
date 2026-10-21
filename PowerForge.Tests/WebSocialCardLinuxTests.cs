using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSocialCardLinuxTests
{
    [Theory]
    [InlineData("build")]
    [InlineData("apidocs")]
    public async Task RequiredCards_LinuxWithoutImageMagick_FailsPipeline(string task)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var root = Path.Combine(Path.GetTempPath(), "pf-social-linux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\n---\nHome.");
            File.WriteAllText(Path.Combine(root, "site.json"), """
                {"Name":"Example","BaseUrl":"https://example.test","Social":{"AutoGenerateCards":true},"Collections":[{"Name":"pages","Input":"content","Output":"/"}]}
                """);
            File.WriteAllText(Path.Combine(root, "api.xml"), "<doc><assembly><name>Demo</name></assembly><members/></doc>");
            var extra = task == "apidocs" ? ",\"xml\":\"api.xml\",\"format\":\"html\"" : string.Empty;
            File.WriteAllText(Path.Combine(root, "pipeline.json"), $$"""
                {"steps":[{"task":"{{task}}","config":"site.json","out":"_site","socialAutoGenerate":true,"socialCardsRequired":true{{extra}}}]}
                """);
            var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet"));
            var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
            start.ArgumentList.Add(typeof(WebPipelineRunner).Assembly.Location);
            foreach (var arg in new[] { "pipeline", "--config", Path.Combine(root, "pipeline.json"), "--mode", "dev" })
                start.ArgumentList.Add(arg);
            // Isolate renderer discovery in the child. Do not mutate PATH in the parallel test host.
            start.Environment["PATH"] = Path.Combine(root, "no-tools");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var text = await output + await error;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("not rendered", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ImageMagick", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
