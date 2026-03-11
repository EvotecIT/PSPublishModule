using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class AboutTopicTemplateServiceTests
{
    [Fact]
    public void Preview_NormalizesTopicAndResolvesRelativeOutputPath()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new AboutTopicTemplateService();
            var result = service.Preview(new AboutTopicTemplateRequest
            {
                TopicName = "Troubleshooting Guide",
                OutputPath = @".\Help\About",
                WorkingDirectory = root
            });

            Assert.Equal("about_Troubleshooting_Guide", result.TopicName);
            Assert.Equal(Path.Combine(root, "Help", "About"), result.OutputDirectory);
            Assert.Equal(Path.Combine(root, "Help", "About", "about_Troubleshooting_Guide.help.txt"), result.FilePath);
            Assert.False(result.Exists);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Generate_WritesMarkdownTemplateAndPreservesPreviewExistence()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new AboutTopicTemplateService();
            var request = new AboutTopicTemplateRequest
            {
                TopicName = "about_Configuration",
                OutputPath = "Help\\About",
                ShortDescription = "Configuration guidance.",
                Format = AboutTopicTemplateFormat.Markdown,
                WorkingDirectory = root
            };

            var result = service.Generate(request);

            Assert.False(result.Exists);
            Assert.True(File.Exists(result.FilePath));
            var content = File.ReadAllText(result.FilePath);
            Assert.Contains("topic: about_Configuration", content, StringComparison.Ordinal);
            Assert.Contains("Configuration guidance.", content, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
