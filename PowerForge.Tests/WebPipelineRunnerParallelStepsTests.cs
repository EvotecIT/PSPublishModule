using System;
using System.IO;
using PowerForge.Tests;
using PowerForge.Web.Cli;
using Xunit;

[Collection(ProcessEnvironmentCollection.Name)]
public class WebPipelineRunnerParallelStepsTests
{
    [Fact]
    public void RunPipeline_MaxParallelism_RunsInGroupDependencyFirstAndKeepsPipelineOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-parallel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalOut = Console.Out;
        var console = new StringWriter();

        try
        {
            File.WriteAllText(Path.Combine(root, "alpha.xml"), BuildMinimalXml("Alpha", "T:Alpha.Widget"));
            File.WriteAllText(Path.Combine(root, "beta-one.xml"), BuildMinimalXml("BetaOne", "T:BetaOne.Widget"));
            File.WriteAllText(Path.Combine(root, "beta-two.xml"), BuildMinimalXml("BetaTwo", "T:BetaTwo.Widget"));
            File.WriteAllText(Path.Combine(root, "gamma.xml"), BuildMinimalXml("Gamma", "T:Gamma.Widget"));
            var pipelinePath = Path.Combine(root, "pipeline.json");
            // Gamma depends on alpha's generated page: it uses that page as its header.
            File.WriteAllText(pipelinePath,
                """
                {
                  "maxParallelism": 3,
                  "profile": true,
                  "profilePath": "./_temp/profile.json",
                  "steps": [
                    { "task": "apidocs", "id": "alpha", "type": "CSharp", "format": "html", "xml": "./alpha.xml", "out": "./_site/api/alpha", "baseUrl": "/api/alpha", "title": "AlphaMarkerTitle" },
                    { "task": "apidocs", "id": "beta", "type": "CSharp", "format": "json", "inputs": [
                      { "id": "beta-one", "xml": "./beta-one.xml", "out": "./_site/api/beta-one", "baseUrl": "/api/beta-one", "title": "Beta One API" },
                      { "id": "beta-two", "xml": "./beta-two.xml", "out": "./_site/api/beta-two", "baseUrl": "/api/beta-two", "title": "Beta Two API" }
                    ] },
                    { "task": "apidocs", "id": "gamma", "dependsOn": "alpha", "type": "CSharp", "format": "html", "xml": "./gamma.xml", "out": "./_site/api/gamma", "baseUrl": "/api/gamma", "title": "Gamma API", "headerHtml": "./_site/api/alpha/index.html" }
                  ]
                }
                """);

            Console.SetOut(console);
            var result = WebPipelineRunner.RunPipeline(pipelinePath, new WebConsoleLogger());
            Console.SetOut(originalOut);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Steps.ConvertAll(step => step.Message)));
            Assert.Equal(3, result.Steps.Count);
            Assert.All(result.Steps, step => Assert.True(step.Success));

            var gammaIndex = File.ReadAllText(Path.Combine(root, "_site", "api", "gamma", "index.html"));
            Assert.Contains("AlphaMarkerTitle", gammaIndex, StringComparison.Ordinal);

            // Each step's buffered block, including batch inputs, is printed whole and in pipeline order.
            var log = console.ToString();
            var markers = new[]
            {
                "Starting [1/3] apidocs", "Finished [1/3] apidocs",
                "Starting [2/3] apidocs", "[2/3] apidocs/beta-one:", "[2/3] apidocs/beta-two:", "Finished [2/3] apidocs",
                "Starting [3/3] apidocs", "Finished [3/3] apidocs"
            };
            var previous = -1;
            foreach (var marker in markers)
            {
                var position = log.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(position > previous, $"Expected '{marker}' after the previous marker in:{Environment.NewLine}{log}");
                previous = position;
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    private static string BuildMinimalXml(string assemblyName, string memberName)
    {
        return
            $"""
            <doc>
              <assembly>
                <name>{assemblyName}</name>
              </assembly>
              <members>
                <member name="{memberName}">
                  <summary>{assemblyName} widget.</summary>
                </member>
              </members>
            </doc>
            """;
    }
}
