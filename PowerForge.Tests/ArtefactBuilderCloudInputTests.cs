using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ArtefactBuilderCloudInputTests
{
    [Theory]
    [InlineData(0x9000001Au, true)]
    [InlineData(0x9000101Au, true)]
    [InlineData(0x9000F01Au, true)]
    [InlineData(0xA000000Cu, false)] // Symbolic link.
    [InlineData(0xA0000003u, false)] // Junction / mount point.
    [InlineData(0x9001001Au, false)] // Outside the documented CLOUD family.
    [InlineData(0x80000021u, false)] // Unused historical ONEDRIVE tag.
    [InlineData(0u, false)]
    public void CloudTagAllowlist_RejectsRedirectionAndUnknownTags(uint tag, bool expected)
        => Assert.Equal(expected, WindowsCloudFiles.IsCloudTag(tag));

    [WindowsFact]
    public void PlaceholderDetection_RecognizesHydratedFilesAndDirectories()
    {
        using var fixture = new WindowsCloudFilesFixture();
        string directory = Directory.CreateDirectory(Path.Combine(fixture.CloudRoot, "module")).FullName;
        string file = Path.Combine(directory, "payload.psm1");
        File.WriteAllText(file, "'cloud-content'");
        Assert.False(WindowsCloudFiles.IsPlaceholder(file));
        fixture.Convert(directory);
        fixture.Convert(file);

        Assert.True(WindowsCloudFiles.IsPlaceholder(directory));
        Assert.True(WindowsCloudFiles.IsPlaceholder(file));
        Assert.Equal("'cloud-content'", File.ReadAllText(file));
        // Finalized archives/signing evidence still require an ordinary local layout.
        Assert.Throws<InvalidOperationException>(() => ArtefactBuilder.ValidateDirectoryTreeContainsNoReparsePoints(directory));
    }

    [WindowsTheory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_CopiesCloudDirectoryAndFileMappings(ArtefactType type)
    {
        using var fixture = new WindowsCloudFilesFixture();
        string project = Directory.CreateDirectory(Path.Combine(fixture.Root, "project")).FullName;
        string staging = Directory.CreateDirectory(Path.Combine(fixture.Root, "staging")).FullName;
        const string module = "CloudInputModule";
        File.WriteAllText(Path.Combine(staging, module + ".psd1"),
            $"@{{ RootModule = '{module}.psm1'; ModuleVersion = '1.0.0' }}");
        File.WriteAllText(Path.Combine(staging, module + ".psm1"), "'main'");
        string assets = Directory.CreateDirectory(Path.Combine(fixture.CloudRoot, "assets")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(assets, "nested")).FullName;
        string content = Path.Combine(nested, "data.txt");
        File.WriteAllText(content, "cloud-directory-content");
        string config = Path.Combine(fixture.CloudRoot, "config.json");
        File.WriteAllText(config, "{\"cloud\":true}");
        foreach (string input in new[] { assets, nested, content, config })
            fixture.Convert(input);

        string output = Path.Combine(fixture.Root, "output");
        var result = new ArtefactBuilder(new NullLogger()).Build(new ConfigurationArtefactSegment
        {
            ArtefactType = type,
            Configuration = new ArtefactConfiguration
            {
                Enabled = true,
                Path = output,
                ArtefactName = "CloudInputModule.zip",
                DirectoryOutput = [new ArtefactCopyMapping { Source = assets, Destination = "assets" }],
                FilesOutput = [new ArtefactCopyMapping { Source = config, Destination = "config.json" }]
            }
        }, project, staging, module, "1.0.0", null, Array.Empty<RequiredModuleReference>());

        string inspectionRoot = output;
        if (type == ArtefactType.ScriptPacked)
        {
            inspectionRoot = Path.Combine(fixture.Root, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);
        }
        Assert.Equal("cloud-directory-content", File.ReadAllText(Path.Combine(inspectionRoot, "assets", "nested", "data.txt")));
        Assert.Equal("{\"cloud\":true}", File.ReadAllText(Path.Combine(inspectionRoot, "config.json")));
        Assert.False((File.GetAttributes(Path.Combine(inspectionRoot, "assets")) & FileAttributes.ReparsePoint) != 0);
    }

    [WindowsFact]
    public void PlaceholderDetection_FailsClosedWhenMetadataCannotBeOpened()
    {
        string path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"), "missing");
        IOException error = Assert.Throws<IOException>(() => WindowsCloudFiles.IsPlaceholder(path));
        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException);
    }

    [WindowsTheory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RejectsJunctionInsideCloudDirectoryBeforeOutputMutation(ArtefactType type)
    {
        using var fixture = new WindowsCloudFilesFixture();
        string project = Directory.CreateDirectory(Path.Combine(fixture.Root, "project")).FullName;
        string staging = Directory.CreateDirectory(Path.Combine(fixture.Root, "staging")).FullName;
        const string module = "CloudLinkConsumer";
        File.WriteAllText(Path.Combine(staging, module + ".psd1"),
            $"@{{ RootModule = '{module}.psm1'; ModuleVersion = '1.0.0' }}");
        File.WriteAllText(Path.Combine(staging, module + ".psm1"), "'main'");
        string assets = Directory.CreateDirectory(Path.Combine(fixture.CloudRoot, "assets")).FullName;
        fixture.Convert(assets);
        string external = Directory.CreateDirectory(Path.Combine(fixture.Root, "external")).FullName;
        File.WriteAllText(Path.Combine(external, "private.txt"), "must-not-publish");
        string junction = Path.Combine(assets, "linked");
        string output = Directory.CreateDirectory(Path.Combine(fixture.Root, "output")).FullName;
        string marker = Path.Combine(output, "keep.txt");
        File.WriteAllText(marker, "preserve-existing-output");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (string argument in new[] { "/d", "/c", "mklink", "/J", junction, external })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdoutRead = process.StandardOutput.ReadToEndAsync();
            var stderrRead = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("Junction creation did not finish.");
            }
            string stdout = stdoutRead.GetAwaiter().GetResult();
            string stderr = stderrRead.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, $"Cannot create junction fixture: {stdout}{stderr}");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).Build(new ConfigurationArtefactSegment
                {
                    ArtefactType = type,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = output,
                        DirectoryOutput = [new ArtefactCopyMapping { Source = assets, Destination = "assets" }]
                    }
                }, project, staging, module, "1.0.0", null, Array.Empty<RequiredModuleReference>()));
            Assert.Contains(junction, error.Message, StringComparison.Ordinal);
            Assert.Equal("preserve-existing-output", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(output, "assets", "linked", "private.txt")));
        }
        finally
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
        }
    }

    [WindowsTheory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_BundlesInstalledDependencyFromCloudFolder(ArtefactType type)
    {
        using var fixture = new WindowsCloudFilesFixture();
        string? originalModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            const string module = "CloudDependencyConsumer";
            const string dependency = "CloudInstalledDependency";
            string project = Directory.CreateDirectory(Path.Combine(fixture.Root, "project")).FullName;
            string staging = Directory.CreateDirectory(Path.Combine(fixture.Root, "staging")).FullName;
            File.WriteAllText(Path.Combine(staging, module + ".psd1"),
                $"@{{ RootModule = '{module}.psm1'; ModuleVersion = '1.0.0' }}");
            File.WriteAllText(Path.Combine(staging, module + ".psm1"), "'main'");
            string installed = Directory.CreateDirectory(Path.Combine(fixture.CloudRoot, dependency, "1.0.0")).FullName;
            string manifest = Path.Combine(installed, dependency + ".psd1");
            string script = Path.Combine(installed, dependency + ".psm1");
            File.WriteAllText(manifest,
                $"@{{ RootModule = '{dependency}.psm1'; ModuleVersion = '1.0.0'; GUID = '{Guid.NewGuid()}' }}");
            File.WriteAllText(script, "function Get-CloudValue { 'cloud-dependency' }");
            foreach (string input in new[] { installed, manifest, script })
                fixture.Convert(input);
            Environment.SetEnvironmentVariable("PSModulePath", fixture.CloudRoot + Path.PathSeparator + originalModulePath);

            var result = new ArtefactBuilder(new NullLogger()).Build(new ConfigurationArtefactSegment
            {
                ArtefactType = type,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = Path.Combine(fixture.Root, "output"),
                    RequiredModules = new ArtefactRequiredModulesConfiguration
                    {
                        Enabled = true,
                        Source = RequiredModulesSource.Installed
                    }
                }
            }, project, staging, module, "1.0.0", null, [new RequiredModuleReference(dependency, requiredVersion: "1.0.0")]);

            string inspectionRoot = result.OutputPath;
            if (type == ArtefactType.ScriptPacked)
            {
                inspectionRoot = Path.Combine(fixture.Root, "extracted");
                System.IO.Compression.ZipFile.ExtractToDirectory(result.OutputPath, inspectionRoot);
            }
            string delivered = Assert.Single(Directory.GetFiles(inspectionRoot, dependency + ".psm1", SearchOption.AllDirectories));
            Assert.Equal(File.ReadAllText(script), File.ReadAllText(delivered));
            Assert.Equal((FileAttributes)0, File.GetAttributes(delivered) & FileAttributes.ReparsePoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", originalModulePath);
        }
    }
}
