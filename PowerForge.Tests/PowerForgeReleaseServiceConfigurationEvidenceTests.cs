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
        }
        finally
        {
            TryDelete(root);
        }
    }
}
