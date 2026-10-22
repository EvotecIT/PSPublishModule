using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSocialCardDependencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildOverrideCreatesDefaultSocialSettingsWhenAbsent(bool generate)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-social-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "site.json"), "{}");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, System.Text.Json.JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "build", config = "site.json", socialAutoGenerate = generate } }
            }));
            Assert.Equal(generate, WebPipelineRunner.RequiresSocialCardDependencies(pipeline, "ci"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("build", null, false)]
    [InlineData("build", true, false)]
    [InlineData("doctor", null, false)]
    [InlineData("doctor", true, false)]
    [InlineData("apidocs", null, true)]
    [InlineData("project-apidocs", true, true)]
    public void DisabledSocialOutputSkipsSiteSetupButPreservesApiGeneration(string task, bool? generate, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-social-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "base.json"), """{"social":{"autoGenerateCards":true}}""");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"extends":"base.json","social":{"enabled":false}}""");
            var step = new Dictionary<string, object?> { ["task"] = task, ["config"] = "site.json" };
            if (generate is not null) step["socialAutoGenerate"] = generate;
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, System.Text.Json.JsonSerializer.Serialize(new { steps = new[] { step } }));
            Assert.Equal(expected, WebPipelineRunner.RequiresSocialCardDependencies(pipeline, "ci"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("--pipeline-mode", false)]
    [InlineData("--pipelineMode", false)]
    [InlineData("--pipeline-mode", true)]
    [InlineData("--pipelineMode", true)]
    public async Task InspectionPreservesModeAliasesAndOutputContracts(string modeOption, bool json)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-social-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"build","modes":["dev"],"socialAutoGenerate":true}]}""");
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in new[] { typeof(WebPipelineRunner).Assembly.Location, "website-runner",
                "--website-root", root, "--pipeline-config", pipeline, modeOption, "dev", "--check-social-cards" })
                start.ArgumentList.Add(argument);
            if (json) start.ArgumentList.Add("--json");
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await error);
            var text = await output;
            if (json)
            {
                using var document = System.Text.Json.JsonDocument.Parse(text);
                Assert.Equal("web.website-runner", document.RootElement.GetProperty("command").GetString());
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.True(document.RootElement.GetProperty("result").GetBoolean());
            }
            else Assert.Equal("true", text.Trim());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("{\"task\":\"apidocs\",\"socialAutoGenerate\":false,\"inputs\":[{\"socialAutoGenerate\":true}]}", "ci", true)]
    [InlineData("{\"task\":\"apidocs\",\"entries\":[{\"config\":\"site.json\"}]}", "ci", true)]
    [InlineData("{\"task\":\"apidocs\",\"socialAutoGenerate\":true,\"inputs\":[{\"socialAutoGenerate\":false}]}", "ci", false)]
    [InlineData("{\"task\":\"doctor\",\"config\":\"site.json\"}", "ci", true)]
    [InlineData("{\"task\":\"doctor\",\"config\":\"site.json\",\"build\":false}", "ci", false)]
    [InlineData("{\"task\":\"doctor\",\"config\":\"site.json\",\"noBuild\":true}", "ci", false)]
    [InlineData("{\"task\":\"project-apidocs\"}", "ci", true)]
    [InlineData("{\"task\":\"project-apidoc\"}", "ci", true)]
    [InlineData("{\"task\":\"project-api-docs\"}", "ci", true)]
    [InlineData("{\"task\":\"build\",\"config\":\"site.json\",\"modes\":[\"ci\"]}", "", true)]
    [InlineData("{\"task\":\"build\",\"config\":\"site.json\",\"modes\":[\"ci\"]}", " ci ", true)]
    public void DetectsBatchAndBuiltInGenerationPaths(string step, string mode, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-social-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "site.json"), """{"social":{"autoGenerateCards":true}}""");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, "{\"steps\":[" + step + "]}");
            Assert.Equal(expected, WebPipelineRunner.RequiresSocialCardDependencies(pipeline, mode));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("build", true, null, "ci", true)]
    [InlineData("apidocs", true, null, "ci", true)]
    [InlineData("build", false, true, "ci", true)]
    [InlineData("apidocs", true, false, "ci", false)]
    [InlineData("build", false, null, "ci", false)]
    [InlineData("build", true, null, "dev", false)]
    public void DetectsInheritedSiteAndPipelineSettingsForActiveSteps(
        string task, bool siteEnabled, bool? stepEnabled, string stepMode, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-social-dependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "site.base.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Social = new { AutoGenerateCards = siteEnabled } }));
            File.WriteAllText(Path.Combine(root, "site.json"), """{"extends":"site.base.json"}""");
            var step = new Dictionary<string, object?> { ["task"] = task, ["config"] = "site.json", ["modes"] = new[] { stepMode } };
            if (stepEnabled is not null)
                step["socialAutoGenerate"] = stepEnabled.Value;
            File.WriteAllText(Path.Combine(root, "pipeline.base.json"),
                System.Text.Json.JsonSerializer.Serialize(new { steps = new[] { step } }));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"extends":"pipeline.base.json"}""");

            Assert.Equal(expected, WebPipelineRunner.RequiresSocialCardDependencies(pipeline, "ci"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
