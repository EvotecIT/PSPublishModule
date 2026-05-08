using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineExportAssemblyInferenceTests
{
    [Fact]
    public void Plan_InferExportAssembly_FromLegacyProjectName_WhenNotExplicitlyProvided()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExportAssemblies = Array.Empty<string>()
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            ProjectName = "PSParseHTML.PowerShell"
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "PSParseHTML.PowerShell" }, plan.BuildSpec.ExportAssemblies);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_NormalizesLegacyNetProjectPath_WithWindowsSeparators()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var csprojDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "nested", "PSParseHTML.PowerShell"));
            var csprojPath = Path.Combine(csprojDir.FullName, "PSParseHTML.PowerShell.csproj");
            File.WriteAllText(csprojPath, "<Project />");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExportAssemblies = Array.Empty<string>()
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            ProjectName = "PSParseHTML.PowerShell",
                            NETProjectPath = "nested\\PSParseHTML.PowerShell"
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(csprojPath, plan.BuildSpec.CsprojPath);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_CarriesLegacyLibraryCopySettings_IntoBuildSpec()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            ExcludeLibraryFilter = new[] { "Microsoft.CodeAnalysis*" },
                            NETDoNotCopyLibrariesRecursively = true
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "Microsoft.CodeAnalysis*" }, plan.BuildSpec.ExcludeLibraryFilter);
            Assert.True(plan.BuildSpec.DoNotCopyLibrariesRecursively);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_CarriesHandleRuntimes_IntoBuildSpec()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            HandleRuntimes = true
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(plan.BuildSpec.HandleRuntimes);
            Assert.Equal(new[] { "NETHandleRuntimes" }, plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_RecordsMissingCsprojReasons_WhenExplicitBinaryBuildSettingsAreConfigured()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            SyncNETProjectVersion = true,
                            ResolveBinaryConflicts = new ResolveBinaryConflictsConfiguration
                            {
                                ProjectName = "PSParseHTML.PowerShell"
                            }
                        }
                    },
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            Framework = new[] { "net8.0" },
                            BinaryModule = new[] { "PSParseHTML.PowerShell.dll" },
                            ExcludeLibraryFilter = new[] { "System.Management.*.dll" },
                            NETDoNotCopyLibrariesRecursively = true,
                            NETBinaryModuleDocumentation = true
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Equal(
                new[]
                {
                    "SyncNETProjectVersion",
                    "NETFramework",
                    "NETBinaryModule",
                    "ResolveBinaryConflictsName",
                    "NETExcludeLibraryFilter",
                    "NETDoNotCopyLibrariesRecursively",
                    "NETBinaryModuleDocumentation"
                },
                plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DoesNotTreatNetFrameworkAlone_AsMissingCsprojBinaryIntent()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            Framework = new[] { "net8.0" }
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_IgnoresBlankBinaryModuleEntries_WhenDerivingMissingCsprojReasons()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            BinaryModule = new[] { "", "   " }
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_IgnoresBlankExcludeLibraryFilterEntries_WhenDerivingMissingCsprojReasons()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            ExcludeLibraryFilter = new[] { "", "   " }
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_RecordsNetFrameworkReason_WhenFrameworksPairWithExplicitBinaryIntent()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            Framework = new[] { "net8.0" },
                            BinaryModule = new[] { "PSParseHTML.PowerShell.dll" }
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "NETFramework", "NETBinaryModule" }, plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DeserializesLegacyNetAssemblyLoadContextAlias_IntoBuildSpec()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var json = $$"""
            {
              "Build": {
                "Name": "PSParseHTML",
                "SourcePath": "{{projectRoot.FullName.Replace("\\", "\\\\")}}",
                "Version": "1.0.0"
              },
              "Segments": [
                {
                  "Type": "BuildLibraries",
                  "BuildLibraries": {
                    "NETAssemblyLoadContext": true
                  }
                }
              ],
              "Install": {
                "Enabled": false
              }
            }
            """;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new ConfigurationSegmentJsonConverter());

            var spec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, options);
            Assert.NotNull(spec);

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec!);

            Assert.True(plan.BuildSpec.UseAssemblyLoadContext);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_UsesLastBuildLibrariesValue_ForDoNotCopyLibrariesRecursivelyReason()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    DoNotCopyLibrariesRecursively = true
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETDoNotCopyLibrariesRecursively = false
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_UsesLastBuildLibrariesValue_ForBinaryModuleDocumentationReason()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "PSParseHTML",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETBinaryModuleDocumentation = true
                        }
                    },
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETBinaryModuleDocumentation = false
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.True(string.IsNullOrWhiteSpace(plan.BuildSpec.CsprojPath));
            Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
