using System.Collections;
using System.Management.Automation;
using System.Reflection;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeProjectCmdletTests
{
    [Fact]
    public void InvokePowerForgeRelease_Configuration_DoesNotRestrictAppleBuildConfigurations()
    {
        var property = typeof(PSPublishModule.InvokePowerForgeReleaseCommand)
            .GetProperty(nameof(PSPublishModule.InvokePowerForgeReleaseCommand.Configuration));

        Assert.NotNull(property);
        Assert.DoesNotContain(
            property!.GetCustomAttributes(inherit: true),
            attribute => attribute.GetType().FullName == "System.Management.Automation.ValidateSetAttribute");
    }

    [Fact]
    public void ExportConfigurationProject_ExistingFileWithoutForce_ReturnsResourceExistsError()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "project.release.json");
            File.WriteAllText(outputPath, "{}");

            var project = new ConfigurationProject
            {
                Name = "Demo",
                Targets = new[]
                {
                    new ConfigurationProjectTarget
                    {
                        Name = "Cli",
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0"
                    }
                }
            };

            using var ps = CreatePowerShellWithModuleImported();
            ps.AddCommand("Export-ConfigurationProject")
                .AddParameter("Project", project)
                .AddParameter("OutputPath", outputPath);

            var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Contains("Use -Force to overwrite.", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExportConfigurationProject_Force_OverwritesExistingFile()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "project.release.json");
            File.WriteAllText(outputPath, "{}");

            var project = new ConfigurationProject
            {
                Name = "Demo",
                Targets = new[]
                {
                    new ConfigurationProjectTarget
                    {
                        Name = "Cli",
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0"
                    }
                }
            };

            using var ps = CreatePowerShellWithModuleImported();
            ps.AddCommand("Export-ConfigurationProject")
                .AddParameter("Project", project)
                .AddParameter("OutputPath", outputPath)
                .AddParameter("Force");

            var results = ps.Invoke();

            Assert.False(ps.HadErrors);
            Assert.Single(results);
            Assert.Equal(Path.GetFullPath(outputPath), Assert.IsType<string>(results[0].BaseObject));
            Assert.Contains("\"Name\": \"Demo\"", File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void NewConfigurationProjectBuild_EmitsSegment()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationProjectBuild")
            .AddParameter("Name", "Libraries")
            .AddParameter("BuildBeforeModule")
            .AddParameter("UseAsReleaseVersionSource")
            .AddParameter("ProvideLocalNuGetFeed")
            .AddParameter("Build")
            .AddParameter("PublishNuget", false)
            .AddParameter("PublishGitHub", false)
            .AddParameter("CreateReleaseZip", false)
            .AddParameter("Options", new Hashtable { ["StagingPath"] = ".\\Artifacts\\ProjectBuild" });

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segment = Assert.IsType<ConfigurationProjectBuildSegment>(Assert.Single(results).BaseObject);
        Assert.Equal("Libraries", segment.Configuration.Name);
        Assert.Equal(Path.Combine("Build", "project.build.json"), segment.Configuration.ConfigPath);
        Assert.True(segment.Configuration.BuildBeforeModule);
        Assert.True(segment.Configuration.UseAsReleaseVersionSource);
        Assert.True(segment.Configuration.ProvideLocalNuGetFeed);
        Assert.True(segment.Configuration.Enabled);
        Assert.True(segment.Configuration.Build);
        Assert.False(segment.Configuration.PublishNuget);
        Assert.False(segment.Configuration.PublishGitHub);
        Assert.False(segment.Configuration.CreateReleaseZip);
        Assert.Equal(".\\Artifacts\\ProjectBuild", segment.Configuration.Options?["StagingPath"]);
    }

    [Fact]
    public void NewConfigurationPackageBuild_EmitsInlineSegment()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationPackageBuild")
            .AddParameter("RootPath", ".\\Sources")
            .AddParameter("ExpectedVersionMap", new Hashtable { ["HtmlTinkerX"] = "2.0.X" })
            .AddParameter("VersionTracks", new Hashtable
            {
                ["Core"] = new Hashtable
                {
                    ["ExpectedVersion"] = "2.0.X",
                    ["Projects"] = new[] { "HtmlTinkerX" },
                    ["IncludePrerelease"] = true
                }
            })
            .AddParameter("BuildBeforeModule")
            .AddParameter("PublishNuget", false)
            .AddParameter("UseGitHubPackages")
            .AddParameter("GitHubPackagesOwner", "EvotecIT")
            .AddParameter("GitHubIncludeProjectNameInTag", false);

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segment = Assert.IsType<ConfigurationPackageBuildSegment>(Assert.Single(results).BaseObject);
        Assert.Equal(".\\Sources", segment.Configuration.RootPath);
        Assert.Equal("2.0.X", segment.Configuration.ExpectedVersionMap?["HtmlTinkerX"]);
        Assert.True(segment.Configuration.BuildBeforeModule);
        Assert.False(segment.Configuration.PublishNuget);
        Assert.True(segment.Configuration.UseGitHubPackages);
        Assert.Equal("EvotecIT", segment.Configuration.GitHubPackagesOwner);
        Assert.False(segment.Configuration.GitHubIncludeProjectNameInTag);
        Assert.NotNull(segment.Configuration.VersionTracks);
        var track = segment.Configuration.VersionTracks!["Core"];
        Assert.Equal("2.0.X", track.ExpectedVersion);
        Assert.Equal(new[] { "HtmlTinkerX" }, track.Projects);
        Assert.True(track.IncludePrerelease);
    }

    [Fact]
    public void NewConfigurationPackageBuild_MirrorsProjectBuildJsonOptions()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Schemas",
            "project.build.schema.json"));

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schemaProperties = schema.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(static name => name != "$schema")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var projectBuildProperties = WritablePropertyNames(typeof(ProjectBuildConfiguration));
        var packageBuildProperties = WritablePropertyNames(typeof(PackageBuildConfiguration));
        var cmdletProperties = WritablePropertyNames(typeof(PSPublishModule.NewConfigurationPackageBuildCommand));

        Assert.Equal(schemaProperties, projectBuildProperties);
        Assert.Empty(projectBuildProperties.Except(packageBuildProperties, StringComparer.Ordinal));
        Assert.Empty(packageBuildProperties.Except(cmdletProperties, StringComparer.Ordinal));
    }

    [Fact]
    public void NewConfigurationRelease_EmitsSegment()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationRelease")
            .AddParameter("VersionSource", ReleaseVersionSource.PackageBuild)
            .AddParameter("PrimaryProject", "HtmlTinkerX")
            .AddParameter("BuildOrder", new[] { "PackageBuild", "Module" })
            .AddParameter("PublishOrder", new[] { "NuGet", "PowerShellGallery", "GitHub" });

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segment = Assert.IsType<ConfigurationReleaseSegment>(Assert.Single(results).BaseObject);
        Assert.Equal(Path.Combine("Artefacts", "UploadReady"), segment.Configuration.StageRoot);
        Assert.Equal(ReleaseVersionSource.PackageBuild, segment.Configuration.VersionSource);
        Assert.Equal("HtmlTinkerX", segment.Configuration.PrimaryProject);
        Assert.Equal(new[] { "PackageBuild", "Module" }, segment.Configuration.BuildOrder);
        Assert.Equal(new[] { "NuGet", "PowerShellGallery", "GitHub" }, segment.Configuration.PublishOrder);
    }

    [Fact]
    public void NewConfigurationGate_EmitsSegment()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationGate")
            .AddParameter("Type", ConfigurationGateMode.Build);

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segment = Assert.IsType<ConfigurationGateSegment>(Assert.Single(results).BaseObject);
        Assert.Equal(ConfigurationGateMode.Build, segment.Configuration.Mode);
    }

    [Fact]
    public void NewConfigurationExternalAsset_EmitsSegment()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript("""
            $file = New-ConfigurationExternalAssetFile -Runtime netcore -Architecture x64 -FileName tool.zip -Uri 'https://example.test/tool.zip'
            New-ConfigurationExternalAsset -Name VendorTool -Version '1.2.3' -OutputPath 'Artefacts\VendorTool' -Source 'https://example.test/vendor-tool' -License 'MIT' -SkipDownload -Files @($file)
            """);

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segment = Assert.IsType<ConfigurationExternalAssetSegment>(Assert.Single(results).BaseObject);
        Assert.Equal("VendorTool", segment.Configuration.Name);
        Assert.Equal("1.2.3", segment.Configuration.Version);
        Assert.Equal("Artefacts\\VendorTool", segment.Configuration.OutputPath);
        Assert.True(segment.Configuration.SkipDownload);
        var file = Assert.Single(segment.Configuration.Files);
        Assert.Equal("netcore", file.Runtime);
        Assert.Equal("x64", file.Architecture);
        Assert.Equal("tool.zip", file.FileName);
        Assert.Equal("https://example.test/tool.zip", file.Uri);
    }

    [Fact]
    public void NewConfigurationModuleBuildProfile_EmitsReusableStandardSegments()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationModuleBuildProfile")
            .AddParameter("Documentation", false)
            .AddParameter("SignModule", false);

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segments = results.Select(static result => result.BaseObject).OfType<IConfigurationSegment>().ToArray();
        Assert.Contains(segments, static segment => segment is ConfigurationFormattingSegment);
        Assert.Contains(segments, static segment => segment is ConfigurationValidationSegment);
        Assert.Contains(segments, static segment => segment is ConfigurationFileConsistencySegment);
        Assert.Contains(segments, static segment => segment is ConfigurationCompatibilitySegment);
        Assert.Contains(segments, static segment => segment is ConfigurationImportModulesSegment);
        Assert.Contains(segments, static segment => segment is ConfigurationBuildSegment);
        Assert.DoesNotContain(segments, static segment => segment is ConfigurationDocumentationSegment);

        var build = segments.OfType<ConfigurationBuildSegment>().Single().BuildModule;
        Assert.True(build.Enable);
        Assert.True(build.Merge);
        Assert.Null(build.MergeMissing);
        Assert.False(build.SignMerged);
        Assert.True(build.InstallMissingModules);
        Assert.Equal(InstallationStrategy.AutoRevision, build.VersionedInstallStrategy);
        Assert.Equal(3, build.VersionedInstallKeep);
    }

    [Fact]
    public void NewConfigurationModuleBuildProfile_EmitsApprovedModuleMergeOnlyWhenRequested()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationModuleBuildProfile")
            .AddParameter("Documentation", false)
            .AddParameter("MergeFunctionsFromApprovedModules", true);

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var build = results
            .Select(static result => result.BaseObject)
            .OfType<ConfigurationBuildSegment>()
            .Single()
            .BuildModule;
        Assert.True(build.MergeMissing);
    }

    [Fact]
    public void NewConfigurationModuleBuildProfile_BinaryRequiresProjectIdentity()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationModuleBuildProfile")
            .AddParameter("Profile", ModuleBuildProfileKind.Binary);

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("NETProjectPath and NETProjectName are required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewConfigurationModuleBuildProfile_EmitsBinaryBuildLibraries()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ConfigurationModuleBuildProfile")
            .AddParameter("Profile", ModuleBuildProfileKind.Binary)
            .AddParameter("Documentation", false)
            .AddParameter("NETProjectName", "DemoModule")
            .AddParameter("NETProjectPath", "Sources\\DemoModule")
            .AddParameter("NETFramework", new[] { "net8.0", "net472" })
            .AddParameter("ResolveBinaryConflicts")
            .AddParameter("ResolveBinaryConflictsName", "DemoModule")
            .AddParameter("NETAssemblyLoadContext");

        var results = ps.Invoke();

        Assert.False(ps.HadErrors);
        var segments = results.Select(static result => result.BaseObject).OfType<IConfigurationSegment>().ToArray();
        var libraries = segments.OfType<ConfigurationBuildLibrariesSegment>().Single().BuildLibraries;
        Assert.True(libraries.Enable);
        Assert.Equal("DemoModule", libraries.ProjectName);
        Assert.Equal("Sources\\DemoModule", libraries.NETProjectPath);
        Assert.Equal(new[] { "net8.0", "net472" }, libraries.Framework);
        Assert.True(libraries.UseAssemblyLoadContext);

        var build = segments.OfType<ConfigurationBuildSegment>().Single().BuildModule;
        Assert.Equal("DemoModule", build.ResolveBinaryConflicts?.ProjectName);
    }

    [Fact]
    public void GetConfigurationBoolean_UsesEnvironmentValueOrDefault()
    {
        var name = "POWERFORGE_TEST_BOOL_" + Guid.NewGuid().ToString("N");
        try
        {
            using (var ps = CreatePowerShellWithModuleImported())
            {
                ps.AddCommand("Get-ConfigurationBoolean")
                    .AddArgument(name)
                    .AddParameter("Default", true);

                var results = ps.Invoke();

                Assert.False(ps.HadErrors);
                Assert.True(Assert.IsType<bool>(Assert.Single(results).BaseObject));
            }

            Environment.SetEnvironmentVariable(name, "false");

            using (var ps = CreatePowerShellWithModuleImported())
            {
                ps.AddCommand("Get-ConfigurationBoolean")
                    .AddArgument(name)
                    .AddParameter("Default", true);

                var results = ps.Invoke();

                Assert.False(ps.HadErrors);
                Assert.False(Assert.IsType<bool>(Assert.Single(results).BaseObject));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(PSPublishModule.ExportConfigurationProjectCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));

        ps.Commands.Clear();
        return ps;
    }

    private static string[] WritablePropertyNames(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanRead && property.CanWrite)
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeProjectCmdlets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
