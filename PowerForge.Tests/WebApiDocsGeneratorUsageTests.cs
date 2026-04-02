using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorUsageTests
{
    [Fact]
    public void GenerateDocsHtml_AndJson_RenderReverseUsageForReferencedTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGeneratorUsageTests).Assembly.Location;
        var producerType = typeof(ApiUsageProducer).FullName!;
        var targetType = typeof(ApiUsageTarget).FullName!;
        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            $"""
            <doc>
              <assembly><name>UsageTests</name></assembly>
              <members>
                <member name="T:{producerType}">
                  <summary>Produces and accepts usage targets.</summary>
                </member>
                <member name="T:{targetType}">
                  <summary>Usage target.</summary>
                </member>
                <member name="M:{producerType}.Create({targetType})">
                  <summary>Creates or returns a target.</summary>
                </member>
                <member name="M:{producerType}.Use({targetType})">
                  <summary>Consumes a target.</summary>
                </member>
                <member name="P:{producerType}.Target">
                  <summary>Current target.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            Format = "both",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var targetHtmlPath = Path.Combine(outputPath, "apiusagetarget", "index.html");
            Assert.True(File.Exists(targetHtmlPath), "Expected target type HTML page to be generated.");
            var html = File.ReadAllText(targetHtmlPath);

            Assert.Contains("id=\"usage\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Returned or exposed by", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Accepted by parameters", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ApiUsageProducer.Create", html, StringComparison.Ordinal);
            Assert.Contains("ApiUsageProducer.Target", html, StringComparison.Ordinal);
            Assert.Contains("ApiUsageProducer.Use", html, StringComparison.Ordinal);

            var targetJsonPath = Path.Combine(outputPath, "types", "apiusagetarget.json");
            Assert.True(File.Exists(targetJsonPath), "Expected target type JSON page to be generated.");
            var json = File.ReadAllText(targetJsonPath);

            Assert.Contains("\"usage\":", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"returnedOrExposedBy\":", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"acceptedByParameters\":", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"ownerType\": \"{producerType}\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}

public sealed class ApiUsageProducer
{
    public ApiUsageTarget Target => new();

    public ApiUsageTarget Create(ApiUsageTarget input) => input;

    public void Use(ApiUsageTarget target)
    {
        _ = target;
    }
}

public sealed class ApiUsageTarget
{
}
