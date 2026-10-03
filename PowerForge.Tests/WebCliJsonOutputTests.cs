using System.Diagnostics;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCliJsonOutputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineJsonOutput_IsParseableWithProgressAndFailureLogs(bool fail)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-json-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nHome");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"collections":[{"name":"pages","input":"content","output":"/"}]}""");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "build", config = "site.json", @out = fail ? "." : "_site", clean = true } }
            }));
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root
            };
            foreach (var arg in new[] { typeof(WebPipelineRunner).Assembly.Location, "pipeline", "--config", pipeline, "--output", "json" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            using var document = JsonDocument.Parse(await stdout);
            Assert.Equal(!fail, document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(fail ? 1 : 0, process.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(await stderr));
            Assert.True(File.Exists(pipeline));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
