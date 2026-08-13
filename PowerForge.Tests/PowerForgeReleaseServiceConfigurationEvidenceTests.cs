using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_ExternalDotNetToolsTracksReleaseAndPublishConfigurationInputs()
    {
        string root = CreateSandbox();
        try
        {
            string projectPath = Path.Combine(root, "Sample.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            string publishConfigPath = Path.Combine(root, "powerforge.dotnetpublish.json");
            File.WriteAllText(publishConfigPath, """
{
  "Targets": [
    {
      "Name": "Sample.Cli",
      "Kind": "Cli",
      "ProjectPath": "Sample.Cli.csproj",
      "Publish": {
        "Framework": "net10.0",
        "Runtimes": [ "win-x64" ],
        "Style": "PortableCompat"
      }
    }
  ]
}
""", new UTF8Encoding(false));
            string releaseConfigPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(releaseConfigPath, "{}", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(new NullLogger());
            PowerForgeReleaseResult result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishConfigPath = Path.GetFileName(publishConfigPath)
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releaseConfigPath,
                    PlanOnly = true,
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            DotNetPublishPlan plan = Assert.IsType<DotNetPublishPlan>(result.DotNetToolPlan);
            Assert.Equal(
                new[] { publishConfigPath, releaseConfigPath }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                plan.ConfigurationInputPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            Assert.Empty(plan.GeneratedConfigurationInputPaths);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AuthorizedWrapperConfigurationBindsExactLoadedContent()
    {
        string root = CreateSandbox();
        try
        {
            string projectPath = Path.Combine(root, "Sample.Cli.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />", new UTF8Encoding(false));
            string publishConfigPath = Path.Combine(root, "powerforge.dotnetpublish.json");
            File.WriteAllText(publishConfigPath, """
{
  "Targets": [
    {
      "Name": "Sample.Cli",
      "Kind": "Cli",
      "ProjectPath": "Sample.Cli.csproj",
      "Publish": { "Framework": "net10.0", "Runtimes": [ "win-x64" ], "Style": "PortableCompat" }
    }
  ]
}
""", new UTF8Encoding(false));
            string releaseConfigPath = Path.Combine(
                root,
                ".release.authorized.1.2.3.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
            File.WriteAllText(releaseConfigPath, """
{
  "Tools": { "DotNetPublishConfigPath": "powerforge.dotnetpublish.json" }
}
""", new UTF8Encoding(false));
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(releaseConfigPath))).ToLowerInvariant();
            PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(releaseConfigPath);
            var service = new PowerForgeReleaseService(new NullLogger());

            PowerForgeReleaseResult result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releaseConfigPath,
                    GeneratedConfigurationInputSha256 = digest,
                    PlanOnly = true,
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            DotNetPublishPlan plan = Assert.IsType<DotNetPublishPlan>(result.DotNetToolPlan);
            Assert.Equal(new[] { releaseConfigPath }, plan.GeneratedConfigurationInputPaths, StringComparer.OrdinalIgnoreCase);

            Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releaseConfigPath,
                    GeneratedConfigurationInputSha256 = new string('b', 64),
                    PlanOnly = true,
                    ToolsOnly = true
                }));

            string callerConfigPath = Path.Combine(root, "caller.release.json");
            File.Copy(releaseConfigPath, callerConfigPath);
            string callerDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(callerConfigPath))).ToLowerInvariant();
            PowerForgeReleaseSpec callerSpec = PowerForgeReleaseService.LoadConfiguration(callerConfigPath);
            Assert.Throws<InvalidOperationException>(() => service.Execute(
                callerSpec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = callerConfigPath,
                    GeneratedConfigurationInputSha256 = callerDigest,
                    PlanOnly = true,
                    ToolsOnly = true
                }));
        }
        finally
        {
            TryDelete(root);
        }
    }
}
