using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSocialCardDependencyTests
{
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
