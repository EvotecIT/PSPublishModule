using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderCopyMappingTests
{
    [Theory]
    [InlineData(ArtefactType.Unpacked, 0)]
    [InlineData(ArtefactType.Unpacked, 1)]
    [InlineData(ArtefactType.Unpacked, 2)]
    [InlineData(ArtefactType.Packed, 0)]
    [InlineData(ArtefactType.Packed, 1)]
    [InlineData(ArtefactType.Packed, 2)]
    public void Build_DependencyPayloadKindConflictPreservesOutput(ArtefactType type, int mappingKind)
    {
        using var fixture = new Fixture();
        string installed = WriteDependencyFixture(fixture.Root);
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "copy-source")).FullName;
        string file = Path.Combine(source, "replacement.psm1");
        File.WriteAllText(file, "'replacement'");
        var segment = fixture.Segment(type);
        segment.Configuration.RequiredModules.Enabled = true;
        segment.Configuration.RequiredModules.Source = RequiredModulesSource.Installed;
        ArtefactCopyMapping[] mappings = [new()
        {
            Source = mappingKind == 0 ? file : source,
            Destination = mappingKind == 0 ? "PreflightDependency/content" : "PreflightDependency/PreflightDependency.psm1"
        }];
        if (mappingKind == 2)
            segment.Configuration.DirectoryOutput = mappings;
        else
            segment.Configuration.FilesOutput = mappings;
        string? previous = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", installed + Path.PathSeparator + previous);

            var error = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
                segment, fixture.Root, fixture.Staging, "Demo", "1.0.0", null,
                [new("PreflightDependency", requiredVersion: "1.0.0")]));

            Assert.Contains("copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PreflightDependency", error.Message, StringComparison.Ordinal);
            fixture.AssertPreserved();
            Assert.Equal("'dependency'", File.ReadAllText(Path.Combine(installed, "PreflightDependency", "1.0.0", "PreflightDependency.psm1")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previous);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_DependencyFileOverridesAndNewFilesRemainSupported(ArtefactType type)
    {
        using var fixture = new Fixture();
        string installed = WriteDependencyFixture(fixture.Root);
        string source = Path.Combine(fixture.Root, "replacement.psm1");
        File.WriteAllText(source, "'override'");
        var segment = fixture.Segment(type);
        segment.Configuration.RequiredModules.Enabled = true;
        segment.Configuration.RequiredModules.Source = RequiredModulesSource.Installed;
        segment.Configuration.FilesOutput =
        [
            new() { Source = source, Destination = "PreflightDependency/PreflightDependency.psm1" },
            new() { Source = source, Destination = "PreflightDependency/content/new.ps1" }
        ];
        string? previous = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", installed + Path.PathSeparator + previous);
            var result = new ArtefactBuilder(new NullLogger()).Build(segment, fixture.Root, fixture.Staging,
                "Demo", "1.0.0", null, [new("PreflightDependency", requiredVersion: "1.0.0")]);
            string output = result.OutputPath;
            if (type == ArtefactType.Packed)
            {
                output = Path.Combine(fixture.Root, "extracted");
                System.IO.Compression.ZipFile.ExtractToDirectory(result.OutputPath, output);
            }

            Assert.Equal("'override'", File.ReadAllText(Path.Combine(output, "PreflightDependency", "PreflightDependency.psm1")));
            Assert.Equal("'override'", File.ReadAllText(Path.Combine(output, "PreflightDependency", "content", "new.ps1")));
            Assert.Equal("'dependency'", File.ReadAllText(Path.Combine(installed, "PreflightDependency", "1.0.0", "PreflightDependency.psm1")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previous);
        }
    }

    [Fact]
    public void Build_RetainedScriptFinalizationIncludesSupportAndDependencies()
    {
        using var fixture = new Fixture();
        string installed = WriteDependencyFixture(fixture.Root);
        string resources = Directory.CreateDirectory(Path.Combine(fixture.Staging, "Resources")).FullName;
        File.WriteAllText(Path.Combine(resources, "Support.ps1"), "'support'");
        var segment = fixture.Segment(ArtefactType.Script);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.RequiredModules.Enabled = true;
        segment.Configuration.RequiredModules.Source = RequiredModulesSource.Installed;
        string support = Path.Combine(fixture.Output, "Resources", "Support.ps1");
        string dependency = Path.Combine(fixture.Output, "PreflightDependency", "PreflightDependency.psm1");
        string? previous = Environment.GetEnvironmentVariable("PSModulePath");
        bool finalized = false;
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", installed + Path.PathSeparator + previous);

            _ = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(segment, fixture.Root, fixture.Staging,
                "Demo", "1.0.0", null, [new("PreflightDependency", requiredVersion: "1.0.0")], context =>
                {
                    finalized = true;
                    Assert.Contains(context.EntryPointPath, context.FinalizationFilePaths!);
                    Assert.Contains(support, context.FinalizationFilePaths!);
                    Assert.Contains(dependency, context.FinalizationFilePaths!);
                    foreach (string file in context.FinalizationFilePaths!)
                        if (file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
                            File.AppendAllText(file, "\n# finalized");
                    return null;
                });

            Assert.True(finalized);
            Assert.Equal("'support'\n# finalized", File.ReadAllText(support));
            Assert.Equal("'dependency'\n# finalized", File.ReadAllText(dependency));
            Assert.Equal("'support'", File.ReadAllText(Path.Combine(resources, "Support.ps1")));
            Assert.Equal("'dependency'", File.ReadAllText(Path.Combine(installed, "PreflightDependency", "1.0.0", "PreflightDependency.psm1")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previous);
        }
    }

    private static string WriteDependencyFixture(string root)
    {
        string installed = Path.Combine(root, "installed");
        string module = Directory.CreateDirectory(Path.Combine(installed, "PreflightDependency", "1.0.0")).FullName;
        File.WriteAllText(Path.Combine(module, "PreflightDependency.psd1"), "@{RootModule='PreflightDependency.psm1';ModuleVersion='1.0.0'}");
        File.WriteAllText(Path.Combine(module, "PreflightDependency.psm1"), "'dependency'");
        Directory.CreateDirectory(Path.Combine(module, "content"));
        return installed;
    }
}
