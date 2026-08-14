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
            InitializeGitRepository(root);
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
            string sourceConfigPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(sourceConfigPath, """
{
  "Tools": { "DotNetPublishConfigPath": "powerforge.dotnetpublish.json" }
}
""", new UTF8Encoding(false));
            string evidenceRoot = CreateSandbox();
            string releaseConfigPath = Path.Combine(
                evidenceRoot,
                ".release.authorized.1.2.3.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
            File.WriteAllText(releaseConfigPath, """
{
  "Tools": { "DotNetPublishConfigPath": "powerforge.dotnetpublish.json" }
}
""", new UTF8Encoding(false));
            PowerForgeReleaseSpec spec = PowerForgeReleaseService.LoadConfiguration(releaseConfigPath);
            string moduleDirectory = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
            string manifestPath = Path.Combine(moduleDirectory, "Sample.psd1");
            string scriptPath = Path.Combine(root, "Build-Module.ps1");
            File.WriteAllText(manifestPath, "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.2.3' }");
            File.WriteAllText(scriptPath, string.Empty);
            string jsonProvenancePath = Path.Combine(moduleDirectory, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName);
            string signedProvenancePath = Path.Combine(moduleDirectory, PowerForgeModuleSourceAttestationWriter.FileName);
            File.WriteAllText(jsonProvenancePath, "{}");
            File.WriteAllText(signedProvenancePath, "@{}");
            spec.Module = new PowerForgeModuleReleaseOptions
            {
                RepositoryRoot = root,
                ManifestPath = manifestPath,
                ScriptPath = scriptPath,
                ModuleName = "Sample"
            };
            var service = new PowerForgeReleaseService(new NullLogger());

            PowerForgeReleaseResult result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = sourceConfigPath,
                    EffectiveConfigurationPath = releaseConfigPath,
                    PlanOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success);
            DotNetPublishPlan plan = Assert.IsType<DotNetPublishPlan>(result.DotNetToolPlan);
            Assert.Equal(new[] { releaseConfigPath }, plan.GeneratedConfigurationInputPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(sourceConfigPath, plan.ConfigurationInputPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(releaseConfigPath, plan.ConfigurationInputPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(
                new[] { jsonProvenancePath, signedProvenancePath },
                plan.GeneratedProvenancePaths,
                StringComparer.OrdinalIgnoreCase);

            byte[] loadedConfiguration = File.ReadAllBytes(releaseConfigPath);
            File.WriteAllText(releaseConfigPath, "{}", new UTF8Encoding(false));
            Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = sourceConfigPath,
                    EffectiveConfigurationPath = releaseConfigPath,
                    PlanOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish
                }));
            File.WriteAllBytes(releaseConfigPath, loadedConfiguration);

            PowerForgeReleaseSpec forgedSpec = PowerForgeReleaseService.LoadConfiguration(sourceConfigPath);
            Assert.Throws<InvalidOperationException>(() => service.Execute(
                forgedSpec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = sourceConfigPath,
                    EffectiveConfigurationPath = releaseConfigPath,
                    PlanOnly = true,
                    ToolsOnly = true
                }));

            string nestedProjectRoot = Directory.CreateDirectory(Path.Combine(root, "src", "Sample")).FullName;
            string nestedProjectPath = Path.Combine(nestedProjectRoot, "Sample.Cli.csproj");
            File.Copy(projectPath, nestedProjectPath);
            string checkoutConfigurationPath = Path.Combine(
                root,
                ".release.authorized.1.2.3.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json");
            File.WriteAllText(checkoutConfigurationPath, $$"""
{
  "Tools": {
    "DotNetPublish": {
      "ProjectRoot": "{{nestedProjectRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
      "Targets": [
        {
          "Name": "Sample.Cli",
          "Kind": "Cli",
          "ProjectPath": "Sample.Cli.csproj",
          "Publish": { "Framework": "net10.0", "Runtimes": [ "win-x64" ], "Style": "PortableCompat" }
        }
      ]
    }
  }
}
""", new UTF8Encoding(false));
            PowerForgeReleaseSpec checkoutConfiguration = PowerForgeReleaseService.LoadConfiguration(checkoutConfigurationPath);
            InvalidOperationException checkoutException = Assert.Throws<InvalidOperationException>(() => service.Execute(
                checkoutConfiguration,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = sourceConfigPath,
                    EffectiveConfigurationPath = checkoutConfigurationPath,
                    PlanOnly = true,
                    ToolsOnly = true
                }));
            Assert.Contains("outside the release checkout", checkoutException.Message, StringComparison.OrdinalIgnoreCase);

            string callerConfigPath = Path.Combine(root, "caller.release.json");
            File.Copy(releaseConfigPath, callerConfigPath);
            PowerForgeReleaseSpec callerSpec = PowerForgeReleaseService.LoadConfiguration(callerConfigPath);
            Assert.Throws<InvalidOperationException>(() => service.Execute(
                callerSpec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = callerConfigPath,
                    EffectiveConfigurationPath = callerConfigPath,
                    PlanOnly = true,
                    ToolsOnly = true
                }));
            TryDelete(evidenceRoot);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void InitializeGitRepository(string root)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("init");
        process.StartInfo.ArgumentList.Add("--quiet");
        Assert.True(process.Start());
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
    }
}
