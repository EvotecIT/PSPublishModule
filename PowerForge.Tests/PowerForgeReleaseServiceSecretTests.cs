using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("{\"GitHub\":{\"Token\":\"secret\"}}", "$.GitHub.Token")]
    [InlineData("{\"Winget\":{\"Submission\":{\"Token\":\"secret\"}}}", "$.Winget.Submission.Token")]
    [InlineData("{\"VirusTotal\":{\"ApiKey\":\"secret\"}}", "$.VirusTotal.ApiKey")]
    [InlineData("{\"Packages\":{\"PublishApiKey\":\"secret\"}}", "$.Packages.PublishApiKey")]
    [InlineData("{\"Tools\":{\"DotNetPublish\":{\"DotNet\":{\"EnvironmentVariables\":{\"PRIVATE_TOKEN\":{\"Value\":\"secret\",\"Secret\":true}}}}}}", "$.Tools.DotNetPublish.DotNet.EnvironmentVariables.PRIVATE_TOKEN.Value")]
    public void Execute_InlineSecret_RejectsAtSharedServiceBoundary(string json, string expectedPath)
    {
        string root = CreateSandbox();
        try
        {
            string path = Path.Combine(root, "release.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(path);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest { ConfigPath = path, PlanOnly = true }));

            Assert.Contains(expectedPath, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Inline release secrets are not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_SecretReferences_AllowsSharedServiceBoundary()
    {
        string root = CreateSandbox();
        try
        {
            string path = Path.Combine(root, "release.json");
            File.WriteAllText(
                path,
                "{\"GitHub\":{\"TokenFilePath\":\"token.txt\"},\"Winget\":{\"Submission\":{\"TokenEnvName\":\"WINGET_TOKEN\"}},\"VirusTotal\":{\"ApiKeyFilePath\":\"vt.txt\"},\"Tools\":{\"DotNetPublish\":{\"DotNet\":{\"EnvironmentVariables\":{\"PRIVATE_TOKEN\":{\"FromEnvironmentVariable\":\"PRIVATE_TOKEN\",\"Secret\":true}}}}}}",
                new UTF8Encoding(false));
            PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(path);

            Exception? exception = Record.Exception(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest { ConfigPath = path, PlanOnly = true }));

            Assert.NotNull(exception);
            Assert.DoesNotContain("Inline release secrets are not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ReferencedDotNetPublishInlineSecret_RejectsBeforePlanningOrPublication()
    {
        string root = CreateSandbox();
        try
        {
            string projectPath = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />", new UTF8Encoding(false));
            string publishPath = Path.Combine(root, "publish.json");
            File.WriteAllText(
                publishPath,
                "{\"DotNet\":{\"EnvironmentVariables\":{\"PRIVATE_TOKEN\":{\"Value\":\"secret\",\"Secret\":true}}},\"Targets\":[{\"Name\":\"Sample\",\"Kind\":\"Cli\",\"ProjectPath\":\"Sample.csproj\",\"Publish\":{\"Framework\":\"net10.0\",\"Runtimes\":[\"win-x64\"],\"Style\":\"PortableCompat\"}}]}",
                new UTF8Encoding(false));
            string releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(
                releasePath,
                "{\"Tools\":{\"DotNetPublishConfigPath\":\"publish.json\"}}",
                new UTF8Encoding(false));
            PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(releasePath);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = releasePath,
                        PlanOnly = true,
                        ToolsOnly = true
                    }));

            Assert.Contains("$.DotNet.EnvironmentVariables.PRIVATE_TOKEN.Value", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Inline release secrets are not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
