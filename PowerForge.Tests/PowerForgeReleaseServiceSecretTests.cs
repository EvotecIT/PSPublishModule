using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void LoadConfiguration_UnknownInlineSecret_RejectsRawJsonBeforeTypedPropertiesAreDiscarded()
    {
        string root = CreateSandbox();
        try
        {
            string path = Path.Combine(root, "release.json");
            File.WriteAllText(
                path,
                "{\"Deployment\":{\"Token\":\"secret\"}}",
                new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.LoadConfiguration(path));

            Assert.Contains("$.Deployment.Token", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Inline release secrets are not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_EffectiveConfigurationUnknownInlineSecret_RejectsBeforeEvidencePublication()
    {
        string root = CreateSandbox();
        try
        {
            string sourcePath = Path.Combine(root, "release.json");
            string effectivePath = Path.Combine(
                root,
                ".release.authorized.1.2.3.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
            File.WriteAllText(sourcePath, "{}", new UTF8Encoding(false));
            File.WriteAllText(
                effectivePath,
                "{\"Deployment\":{\"Token\":\"secret\"}}",
                new UTF8Encoding(false));
            var spec = new PowerForgeReleaseSpec
            {
                LoadedConfigurationPath = effectivePath,
                LoadedConfigurationSha256 = new string('0', 64)
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = sourcePath,
                        EffectiveConfigurationPath = effectivePath,
                        PlanOnly = true
                    }));

            Assert.Contains("$.Deployment.Token", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Inline release secrets are not allowed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("{\"GitHub\":{\"Token\":\"secret\"}}", "$.GitHub.Token")]
    [InlineData("{\"Winget\":{\"Submission\":{\"Token\":\"secret\"}}}", "$.Winget.Submission.Token")]
    [InlineData("{\"VirusTotal\":{\"ApiKey\":\"secret\"}}", "$.VirusTotal.ApiKey")]
    [InlineData("{\"Packages\":{\"PublishApiKey\":\"secret\"}}", "$.Packages.PublishApiKey")]
    [InlineData("{\"Tools\":{\"DotNetPublish\":{\"DotNet\":{\"EnvironmentVariables\":{\"PRIVATE_TOKEN\":{\"Value\":\"secret\",\"Secret\":true}}}}}}", "$.Tools.DotNetPublish.DotNet.EnvironmentVariables.PRIVATE_TOKEN.Value")]
    [InlineData("{\"Tools\":{\"DotNetPublish\":{\"Hooks\":[{\"Id\":\"publish\",\"Command\":\"pwsh\",\"Environment\":{\"GITHUB_TOKEN\":\"secret\"}}]}}}", "$.Tools.DotNetPublish.Hooks[0].Environment.GITHUB_TOKEN")]
    [InlineData("{\"Tools\":{\"DotNetPublish\":{\"Hooks\":[{\"Id\":\"publish\",\"Command\":\"pwsh\",\"Environment\":{\"NUGET_API_KEY\":\"secret\"}}]}}}", "$.Tools.DotNetPublish.Hooks[0].Environment.NUGET_API_KEY")]
    [InlineData("{\"Tools\":{\"DotNetPublish\":{\"Hooks\":[{\"Id\":\"publish\",\"Command\":\"pwsh\",\"Environment\":{\"SIGNING_PASSWORD\":\"secret\"}}]}}}", "$.Tools.DotNetPublish.Hooks[0].Environment.SIGNING_PASSWORD")]
    [InlineData("{\"Deployment\":{\"secret\":true,\"value\":\"credential\"}}", "$.Deployment.Value")]
    [InlineData("{\"Deployment\":{\"Secret\":false,\"secret\":true,\"Value\":\"credential\"}}", "$.Deployment.Value")]
    public void Execute_InlineSecret_RejectsAtSharedServiceBoundary(string json, string expectedPath)
    {
        string root = CreateSandbox();
        try
        {
            string path = Path.Combine(root, "release.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(path);
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest { ConfigPath = path, PlanOnly = true });
            });

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
