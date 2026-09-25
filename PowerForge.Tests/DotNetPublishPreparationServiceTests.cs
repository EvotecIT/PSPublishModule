using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetPublishPreparationServiceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Prepare_no_sign_disables_only_selected_publish_target_signing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-no-sign-" + Guid.NewGuid().ToString("N")));
        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": { "projectRoot": "." },
  "signingProfiles": {
    "Release": { "enabled": true, "thumbprint": "0123456789ABCDEF0123456789ABCDEF01234567" }
  },
  "targets": [
    { "name": "Inline", "publish": { "sign": { "enabled": true, "thumbprint": "0123456789ABCDEF0123456789ABCDEF01234567" } } },
    { "name": "Profile", "publish": { "signProfile": "Release" } },
    { "name": "OverridesOnly", "publish": { "signOverrides": { "enabled": true, "thumbprint": "0123456789ABCDEF0123456789ABCDEF01234567" } } }
  ],
  "installers": [
    { "id": "Installer", "prepareFromTarget": "Inline", "sign": { "enabled": true } }
  ]
}
""");

            DotNetPublishPreparedContext Prepare(string target, bool noPublishSign) =>
                new DotNetPublishPreparationService(new NullLogger()).Prepare(new DotNetPublishPreparationRequest
                {
                    ParameterSetName = "Config",
                    CurrentPath = root.FullName,
                    ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                    ConfigPath = configPath,
                    Target = [target],
                    NoPublishSign = noPublishSign
                });

            var configured = Prepare("Inline", noPublishSign: false);
            Assert.True(Assert.Single(configured.Spec.Targets).Publish.Sign!.Enabled);

            var inlineSmoke = Prepare("Inline", noPublishSign: true);
            Assert.False(Assert.Single(inlineSmoke.Spec.Targets).Publish.Sign!.Enabled);
            Assert.True(Assert.Single(inlineSmoke.Spec.Installers).Sign!.Enabled);

            var profileSmoke = Prepare("Profile", noPublishSign: true);
            var profileTarget = Assert.Single(profileSmoke.Spec.Targets);
            Assert.True(profileSmoke.Spec.SigningProfiles!["Release"].Enabled);
            Assert.False(DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                profileSmoke.Spec.SigningProfiles,
                profileTarget.Publish.SignProfile,
                profileTarget.Publish.Sign,
                profileTarget.Publish.SignOverrides,
                "Profile")!.Enabled);

            var overridesOnlySmoke = Prepare("OverridesOnly", noPublishSign: true);
            var overridesOnlyTarget = Assert.Single(overridesOnlySmoke.Spec.Targets);
            Assert.False(DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                overridesOnlySmoke.Spec.SigningProfiles,
                overridesOnlyTarget.Publish.SignProfile,
                overridesOnlyTarget.Publish.Sign,
                overridesOnlyTarget.Publish.SignOverrides,
                "OverridesOnly")!.Enabled);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Prepare_no_sign_rejects_selected_bundle_before_changing_signing()
    {
        var spec = new DotNetPublishSpec
        {
            Targets = [new DotNetPublishTarget { Name = "App", Publish = new DotNetPublishPublishOptions { Sign = new DotNetPublishSignOptions { Enabled = true } } }],
            Bundles = [new DotNetPublishBundle { Id = "AppBundle", PrepareFromTarget = "App" }]
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            DotNetPublishSigningProfileResolver.DisableSelectedTargetSigning(spec));
        Assert.Contains("bundle signing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(spec.Targets[0].Publish.Sign!.Enabled);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Prepare_no_sign_allows_bundle_excluded_by_active_profile()
    {
        var spec = new DotNetPublishSpec
        {
            Profile = "Smoke",
            Profiles = [new DotNetPublishProfile { Name = "Smoke", Targets = ["Monitoring"] }],
            Targets =
            [
                new DotNetPublishTarget { Name = "Monitoring", Publish = new DotNetPublishPublishOptions { Sign = new DotNetPublishSignOptions { Enabled = true } } },
                new DotNetPublishTarget { Name = "Agent", Publish = new DotNetPublishPublishOptions { Sign = new DotNetPublishSignOptions { Enabled = true } } }
            ],
            Bundles = [new DotNetPublishBundle { Id = "AgentBundle", PrepareFromTarget = "Agent" }]
        };

        var originalMonitoring = spec.Targets[0];
        var originalAgent = spec.Targets[1];
        DotNetPublishSigningProfileResolver.DisableSelectedTargetSigning(spec);

        var selected = DotNetPublishPipelineRunner.ResolveProfile(spec);
        Assert.Empty(selected.Bundles);
        Assert.False(Assert.Single(selected.Targets).Publish.Sign!.Enabled);
        Assert.False(spec.Targets[0].Publish.Sign!.Enabled);
        Assert.True(spec.Targets[1].Publish.Sign!.Enabled);
        Assert.True(originalMonitoring.Publish.Sign!.Enabled);
        Assert.Same(originalAgent, spec.Targets[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Prepare_no_sign_rejects_null_publish_settings(bool useProfile)
    {
        var target = new DotNetPublishTarget { Name = "Monitoring", Publish = null! };
        var spec = new DotNetPublishSpec
        {
            Profile = useProfile ? "Smoke" : null,
            Profiles = useProfile ? [new DotNetPublishProfile { Name = "Smoke", Targets = ["Monitoring"] }] : [],
            Targets = [target]
        };

        var error = Assert.Throws<ArgumentException>(() =>
            DotNetPublishSigningProfileResolver.DisableSelectedTargetSigning(spec));
        Assert.Contains("Target.Publish is required", error.Message, StringComparison.Ordinal);
        Assert.Same(target, Assert.Single(spec.Targets));
        Assert.Null(target.Publish);
    }

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
  "profile": "msi",
  "profiles": [
    {
      "name": "msi",
      "default": true,
      "targets": [ "App", "Tool" ]
    }
  ],
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
      "prepareFromTarget": " App "
    },
    {
      "id": "ToolInstaller",
      "prepareFromTarget": "Tool"
    }
  ],
  "bundles": [
    {
      "id": "AppBundle",
      "prepareFromTarget": " App "
    },
    {
      "id": "ToolBundle",
      "prepareFromTarget": "Tool"
    }
  ],
  "storePackages": [
    {
      "id": "AppStore",
      "prepareFromTarget": " App "
    },
    {
      "id": "ToolStore",
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
                OutputPath = "Artifacts/Portable/{target}/{rid}",
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseLocalHtmlForgeX"] = "true"
                },
                SkipInstallers = true,
                SkipRestore = true,
                SkipBuild = true,
                JsonOnly = true
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Equal(configPath, context.SourceLabel);
            Assert.Equal(Path.Combine(root.FullName, "powerforge.dotnetpublish.generated.json"), context.JsonOutputPath);
            Assert.Single(context.Spec.Targets);
            Assert.Equal("App", context.Spec.Targets[0].Name);
            Assert.Single(context.Spec.Profiles[0].Targets);
            Assert.Equal("App", context.Spec.Profiles[0].Targets[0]);
            Assert.Equal(new[] { "linux-x64" }, context.Spec.Targets[0].Publish.Runtimes);
            Assert.Equal("net10.0", context.Spec.Targets[0].Publish.Framework);
            Assert.Equal(new[] { "net10.0" }, context.Spec.Targets[0].Publish.Frameworks);
            Assert.Equal(DotNetPublishStyle.PortableCompat, context.Spec.Targets[0].Publish.Style);
            Assert.Equal(new[] { DotNetPublishStyle.PortableCompat }, context.Spec.Targets[0].Publish.Styles);
            Assert.Equal("Artifacts/Portable/{target}/{rid}", context.Spec.Targets[0].Publish.OutputPath);
            Assert.Equal("true", context.Spec.DotNet.MsBuildProperties!["UseLocalHtmlForgeX"]);
            Assert.Empty(context.Spec.Installers);
            Assert.Equal("AppBundle", Assert.Single(context.Spec.Bundles).Id);
            Assert.Equal("AppStore", Assert.Single(context.Spec.StorePackages).Id);
            Assert.False(context.Spec.DotNet.Restore);
            Assert.False(context.Spec.DotNet.Build);
            Assert.True(context.Spec.DotNet.NoRestoreInPublish);
            Assert.True(context.Spec.DotNet.NoBuildInPublish);
            Assert.True(context.SkipBuildRequested);
            Assert.True(context.SkipRestoreRequested);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_target_override_removes_unselected_companion_targets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-companion-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": { "projectRoot": "." },
  "targets": [
    { "name": "App", "projectPath": "src/App/App.csproj" },
    { "name": "Portable", "projectPath": "src/Portable/Portable.csproj" }
  ],
  "installers": [
    {
      "id": "AppInstaller",
      "prepareFromTarget": "App",
      "versioning": {
        "enabled": false,
        "additionalPublishTargets": [ " Portable " ]
      }
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
                Target = new[] { "App" }
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Equal("App", Assert.Single(context.Spec.Targets).Name);
            Assert.Empty(Assert.Single(context.Spec.Installers).Versioning!.AdditionalPublishTargets);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_target_override_rejects_unknown_companion_before_pruning()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-unknown-companion-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": { "projectRoot": "." },
  "targets": [
    { "name": "App", "projectPath": "src/App/App.csproj" },
    { "name": "Portable", "projectPath": "src/Portable/Portable.csproj" }
  ],
  "installers": [
    {
      "id": "AppInstaller",
      "prepareFromTarget": "App",
      "versioning": {
        "enabled": true,
        "applyToPublish": true,
        "additionalPublishTargets": [ "Portable", "Missing" ]
      }
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
                Target = new[] { "App" }
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new DotNetPublishPreparationService(new NullLogger()).Prepare(request));

            Assert.Contains("Missing", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Prepare_from_config_rejects_profile_with_no_selected_targets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-profile-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": {
    "projectRoot": "."
  },
  "profile": "tools",
  "profiles": [
    {
      "name": "tools",
      "default": true,
      "targets": [ "Tool" ]
    }
  ],
  "targets": [
    {
      "name": "App",
      "projectPath": "src/App/App.csproj"
    },
    {
      "name": "Tool",
      "projectPath": "src/Tool/Tool.csproj"
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
                Target = new[] { "App" }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => new DotNetPublishPreparationService(new NullLogger()).Prepare(request));

            Assert.Contains("Profile 'tools' does not match target override", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_config_ignores_unselected_profiles_when_filtering_targets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dotnet-publish-profile-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "publish.json");
            File.WriteAllText(configPath, """
{
  "dotNet": {
    "projectRoot": "."
  },
  "profile": "app",
  "profiles": [
    {
      "name": "app",
      "default": true,
      "targets": [ "App" ]
    },
    {
      "name": "tools",
      "targets": [ "Tool" ]
    }
  ],
  "targets": [
    {
      "name": "App",
      "projectPath": "src/App/App.csproj"
    },
    {
      "name": "Tool",
      "projectPath": "src/Tool/Tool.csproj"
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
                Target = new[] { "App" }
            };

            var context = new DotNetPublishPreparationService(new NullLogger()).Prepare(request);

            Assert.Single(context.Spec.Targets);
            Assert.Equal("App", context.Spec.Targets[0].Name);
            var appProfile = Assert.Single(context.Spec.Profiles, profile => string.Equals(profile.Name, "app", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { "App" }, appProfile.Targets);
            var toolsProfile = Assert.Single(context.Spec.Profiles, profile => string.Equals(profile.Name, "tools", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { "Tool" }, toolsProfile.Targets);
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
