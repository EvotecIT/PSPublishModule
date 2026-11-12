using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(PublishTool.ManagedModule, true)]
    [InlineData(PublishTool.ManagedModule, false)]
    [InlineData(PublishTool.PowerShellGet, true)]
    [InlineData(PublishTool.PowerShellGet, false)]
    [InlineData(PublishTool.PSResourceGet, true)]
    [InlineData(PublishTool.PSResourceGet, false)]
    public void Run_RepositoryPublishSignsManifestLoadedContentInFinalLayout(PublishTool tool, bool signModule)
    {
        using var root = new TemporaryDirectory();
        const string moduleName = "TestModule";
        string source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        string feed = Directory.CreateDirectory(Path.Combine(root.Path, "feed")).FullName;
        File.WriteAllText(Path.Combine(source, moduleName + ".psd1"), """
            @{
                ModuleVersion = '1.0.0'
                RootModule = 'TestModule.psm1'
                Author = 'Test author'
                Description = 'Repository signing regression fixture.'
                FormatsToProcess = @('TestModule.format.ps1xml')
                TypesToProcess = @('TestModule.types.ps1xml')
                ScriptsToProcess = @('loaded.ps1')
                NestedModules = @('Nested.psm1')
                FunctionsToExport = @()
                CmdletsToExport = @()
                AliasesToExport = @()
            }
            """);
        string[] loadedFiles =
        [
            "TestModule.psm1", "TestModule.format.ps1xml", "TestModule.types.ps1xml",
            "loaded.ps1", "Nested.psm1"
        ];
        foreach (string name in loadedFiles)
        {
            string content = name.EndsWith(".ps1xml", StringComparison.Ordinal)
                ? "<Configuration />" : "# module content";
            if (name.EndsWith(".types.ps1xml", StringComparison.Ordinal))
                content = "<Types />";
            File.WriteAllText(Path.Combine(source, name), content);
        }

        var transport = new RepositorySigningTransport();
        var hosted = new FakeHostedOperations
        {
            AutoSuccessfulSigningResult = true,
            SatisfyDependencies = true,
            RejectIncompleteBinaryPayload = true,
            RepositoryPublisher = new ModulePublisher(new NullLogger(), transport),
            SigningFilesCompleted = (_, files) =>
            {
                foreach (string file in files)
                {
                    File.AppendAllText(file, Environment.NewLine +
                        (file.EndsWith(".ps1xml", StringComparison.Ordinal)
                            ? "<!-- test-only signature -->" : "# test-only signature"));
                }
            }
        };
        var signing = new SigningOptionsConfiguration
        {
            CertificateThumbprint = "TEST-ONLY",
            Include = ["*.psd1"],
            ExcludePaths = [".ps1xml", "loaded.ps1", "Nested.psm1"],
            OverwriteSigned = false
        };
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = moduleName, SourcePath = source, Version = "1.0.0" },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments =
            [
                new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { SignMerged = signModule } },
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Signing = signing } },
                new ConfigurationInformationSegment
                {
                    Configuration = new InformationConfiguration { IncludeRoot = ["*.ps1", "*.psm1", "*.psd1", "*.ps1xml"] }
                },
                new ConfigurationImportModulesSegment { ImportModules = new ImportModulesConfiguration { Self = true } },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        Tool = tool,
                        RepositoryName = "TestFeed",
                        ApiKey = "test-only",
                        Repository = new PublishRepositoryConfiguration
                        {
                            Name = "TestFeed", Uri = feed, EnsureRegistered = false
                        }
                    }
                }
            ]
        };

        ModulePipelineResult result = CreateRunner(hosted).Run(spec);

        Assert.True(Assert.Single(result.PublishResults).Succeeded);
        Dictionary<string, string> published = transport.PublishedFiles;
        if (tool == PublishTool.ManagedModule)
        {
            using var package = ZipFile.OpenRead(Path.Combine(feed, moduleName + ".1.0.0.nupkg"));
            published = loadedFiles.ToDictionary(name => name, name =>
            {
                var entry = package.GetEntry(name);
                Assert.NotNull(entry);
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            });
        }
        foreach (string name in loadedFiles)
        {
            Assert.Equal(signModule, published[name].Contains("test-only signature", StringComparison.Ordinal));
            Assert.DoesNotContain("test-only signature", File.ReadAllText(Path.Combine(source, name)), StringComparison.Ordinal);
        }
        Assert.Equal(signModule ? 2 : 0, hosted.SignCalls);
        Assert.Equal(signModule ? 1 : 0, hosted.SigningRootPaths.Count(path =>
            path.Contains(Path.Combine("PowerForge", "publish"), StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, hosted.BinaryDependencyRootPaths.Where(path =>
            path.Contains(Path.Combine("PowerForge", "publish"), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(signing.OverwriteSigned);
        Assert.Equal(new[] { "*.psd1" }, signing.Include);
    }

    private sealed class RepositorySigningTransport : IPowerShellRunner
    {
        public Dictionary<string, string> PublishedFiles { get; private set; } = new();

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            string script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPWSGET::AVAILABLE::", StringComparison.Ordinal))
                return new PowerShellRunResult(0, "PFPWSGET::AVAILABLE::1", string.Empty, "test-host");
            if (script.Contains("PFPSRG::AVAILABLE::", StringComparison.Ordinal))
                return new PowerShellRunResult(0, "PFPSRG::AVAILABLE::1", string.Empty, "test-host");
            if (script.Contains("Find-Module", StringComparison.Ordinal) ||
                script.Contains("Find-PSResource", StringComparison.Ordinal))
            {
                return new PowerShellRunResult(0, string.Empty, string.Empty, "test-host");
            }
            bool powerShellGet = script.Contains("Publish-Module @params", StringComparison.Ordinal);
            if (!powerShellGet && !script.Contains("Publish-PSResource @params", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected repository transport invocation.");

            string moduleRoot = request.Arguments[0];
            PublishedFiles = Directory.EnumerateFiles(moduleRoot)
                .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
            return new PowerShellRunResult(0,
                powerShellGet ? "PFPWSGET::PUBLISH::OK" : "PFPSRG::PUBLISH::OK",
                string.Empty, "test-host");
        }
    }
}
