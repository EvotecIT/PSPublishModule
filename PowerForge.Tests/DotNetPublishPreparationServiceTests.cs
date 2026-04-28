using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetPublishPreparationServiceTests
{
    [Fact]
    public void Prepare_from_config_applies_overrides_and_generated_json_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-prepare-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": {
    "projectRoot": ".",
    "restore": true,
    "build": true
  },
  "targets": [
    {
      "name": "App",
      "projectPath": "src/App/App.csproj",
      "publish": {
        "framework": "net8.0",
        "frameworks": [ "net8.0" ],
        "runtimes": [ "win-x64" ],
        "style": "Portable",
        "styles": [ "Portable" ]
      }
    },
    {
      "name": "Tool",
      "projectPath": "src/Tool/Tool.csproj"
    }
  ],
  "installers": [
    {
      "id": "AppInstaller",
      "prepareFromTarget": "App"
    },
    {
      "id": "ToolInstaller",
      "prepareFromTarget": "Tool"
    }
  ]
}
""");

            var request = new DotNetPublishPreparationRequest
            {
                ParameterSetName = "Config",
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                ConfigPath = configPath,
                Target = new[] { "App" },
                Runtimes = new[] { "linux-x64" },
                Frameworks = new[] { "net10.0" },
                Styles = new[] { DotNetPublishStyle.PortableCompat },
                SkipRestore = true,
                SkipBuild = true,
                JsonOnly = true
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Equal(configPath, context.SourceLabel);
            Assert.Equal(Path.Combine(root.FullName, "powerforge.dotnetpublish.generated.json"), context.JsonOutputPath);
            Assert.Single(context.Spec.Targets);
            Assert.Equal("App", context.Spec.Targets[0].Name);
            Assert.Single(context.Spec.Installers);
            Assert.Equal("AppInstaller", context.Spec.Installers[0].Id);
            Assert.Equal(new[] { "linux-x64" }, context.Spec.Targets[0].Publish.Runtimes);
            Assert.Equal("net10.0", context.Spec.Targets[0].Publish.Framework);
            Assert.Equal(new[] { "net10.0" }, context.Spec.Targets[0].Publish.Frameworks);
            Assert.Equal(DotNetPublishStyle.PortableCompat, context.Spec.Targets[0].Publish.Style);
            Assert.Equal(new[] { DotNetPublishStyle.PortableCompat }, context.Spec.Targets[0].Publish.Styles);
            Assert.False(context.Spec.DotNet.Restore);
            Assert.False(context.Spec.DotNet.Build);
            Assert.True(context.Spec.DotNet.NoRestoreInPublish);
            Assert.True(context.Spec.DotNet.NoBuildInPublish);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_settings_defaults_json_path_to_current_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-dsl-" + Guid.NewGuid().ToString("N")));

        try
        {
            var request = new DotNetPublishPreparationRequest
            {
                ParameterSetName = "Settings",
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                JsonOnly = true
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Equal(Path.Combine(root.FullName, "powerforge.dotnetpublish.json"), context.JsonOutputPath);
            Assert.Empty(context.Spec.Targets);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_config_applies_project_root_override()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-root-" + Guid.NewGuid().ToString("N")));

        try
        {
            var repoRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "repo"));
            var configPath = Path.Combine(root.FullName, "Build", "publish.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, """
{
  "dotNet": {
    "projectRoot": ".."
  },
  "targets": []
}
""");

            var request = new DotNetPublishPreparationRequest
            {
                ParameterSetName = "Config",
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                ConfigPath = configPath,
                ProjectRoot = repoRoot.FullName
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Equal(repoRoot.FullName, context.Spec.DotNet.ProjectRoot);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
