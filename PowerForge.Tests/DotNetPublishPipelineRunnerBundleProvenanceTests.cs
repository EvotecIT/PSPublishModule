using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBundleProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_AdmitsBundleInputProducedByDeclaredHook()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config", "user.name", "PowerForge Tests");
            RunGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            string projectPath = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Generated/\nArtifacts/\n");
            RunGit(root, "add", "Sample.csproj", ".gitignore");
            RunGit(root, "commit", "-m", "tracked source");

            string generatedModule = Path.Combine(root, "Generated", "SampleModule");
            Directory.CreateDirectory(generatedModule);
            File.WriteAllText(Path.Combine(generatedModule, "SampleModule.psm1"), "'generated module'");
            DotNetPublishPlan plan = CreatePlan(
                root,
                projectPath,
                "module-include",
                Path.Combine(generatedModule, "SampleModule.psm1"));
            plan.Steps = plan.Steps.Concat(new[]
            {
                new DotNetPublishStep
                {
                    Key = "hook:BeforeBundle:module",
                    Kind = DotNetPublishStepKind.CommandHook,
                    HookId = "module",
                    HookPhase = DotNetPublishCommandHookPhase.BeforeBundle,
                    HookCommand = "pwsh",
                    HookGeneratedOutputs = new[] { "Generated/SampleModule" },
                    HookGeneratedOutputsValidated = true
                }
            }).ToArray();

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty);
        }
        finally
        {
            foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ReadSourceProvenance_DeclaredHookOutputIsTrustedOnlyWhileAbsentOrValidated(
        bool createUnvalidatedOutput,
        bool expectedDirty)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config", "user.name", "PowerForge Tests");
            RunGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            string projectPath = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Generated/\nArtifacts/\n");
            RunGit(root, "add", "Sample.csproj", ".gitignore");
            RunGit(root, "commit", "-m", "tracked source");

            string generatedModule = Path.Combine(root, "Generated", "SampleModule");
            string generatedFile = Path.Combine(generatedModule, "SampleModule.psm1");
            if (createUnvalidatedOutput)
            {
                Directory.CreateDirectory(generatedModule);
                File.WriteAllText(generatedFile, "'partial module'");
            }

            DotNetPublishPlan plan = CreatePlan(root, projectPath, "module-include", generatedFile);
            plan.Steps = plan.Steps.Concat(new[]
            {
                new DotNetPublishStep
                {
                    Key = "hook:BeforeBundle:module",
                    Kind = DotNetPublishStepKind.CommandHook,
                    HookId = "module",
                    HookPhase = DotNetPublishCommandHookPhase.BeforeBundle,
                    HookCommand = "pwsh",
                    HookGeneratedOutputs = new[] { "Generated/SampleModule" }
                }
            }).ToArray();

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.Equal(expectedDirty, provenance.Dirty);
        }
        finally
        {
            foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("copy-item")]
    [InlineData("module-include")]
    [InlineData("generated-template")]
    [InlineData("bundle-script")]
    public void ReadSourceProvenance_BundleInputsMustBeTrackedAndUnchanged(string inputKind)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config", "user.name", "PowerForge Tests");
            RunGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            string projectPath = Path.Combine(root, "Sample.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Ignored/\nArtifacts/\n");
            RunGit(root, "add", "Sample.csproj", ".gitignore");
            RunGit(root, "commit", "-m", "tracked source");

            string ignoredRoot = Directory.CreateDirectory(Path.Combine(root, "Ignored")).FullName;
            string inputPath = inputKind == "module-include"
                ? Path.Combine(ignoredRoot, "Module", "Module.psm1")
                : Path.Combine(ignoredRoot, inputKind + ".ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, "'bundle input v1'");
            DotNetPublishPlan plan = CreatePlan(root, projectPath, inputKind, inputPath);

            DotNetPublishPipelineRunner.SourceProvenance ignored =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
            Assert.True(ignored.Dirty);

            RunGit(root, "add", "-f", "Ignored");
            RunGit(root, "commit", "-m", "tracked bundle input");
            DotNetPublishPipelineRunner.SourceProvenance clean =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
            Assert.False(clean.Dirty, string.Join(Environment.NewLine, clean.DirtyReasons));

            File.WriteAllText(inputPath, "'bundle input mutated during build'");
            DotNetPublishPipelineRunner.SourceProvenance mutated =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
            Assert.True(mutated.Dirty);
        }
        finally
        {
            foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            Directory.Delete(root, recursive: true);
        }
    }

    private static DotNetPublishPlan CreatePlan(
        string root,
        string projectPath,
        string inputKind,
        string inputPath)
    {
        var bundle = new DotNetPublishBundlePlan
        {
            Id = "package",
            PrepareFromTarget = "Sample"
        };
        switch (inputKind)
        {
            case "copy-item":
                bundle.CopyItems =
                [
                    new DotNetPublishBundleCopyItemPlan
                    {
                        SourcePath = inputPath,
                        DestinationPath = "copy.ps1"
                    }
                ];
                break;
            case "module-include":
                bundle.ModuleIncludes =
                [
                    new DotNetPublishBundleModuleIncludePlan
                    {
                        ModuleName = "SampleModule",
                        SourcePath = Path.GetDirectoryName(inputPath)!,
                        DestinationPath = "Modules/{moduleName}"
                    }
                ];
                break;
            case "generated-template":
                bundle.GeneratedScripts =
                [
                    new DotNetPublishBundleGeneratedScriptPlan
                    {
                        TemplatePath = inputPath,
                        OutputPath = "Install.ps1"
                    }
                ];
                break;
            case "bundle-script":
                bundle.Scripts = [new DotNetPublishBundleScriptPlan { Path = inputPath }];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(inputKind));
        }

        return new DotNetPublishPlan
        {
            ProjectRoot = root,
            Configuration = "Release",
            Targets =
            [
                new DotNetPublishTargetPlan
                {
                    Name = "Sample",
                    ProjectPath = projectPath,
                    Combinations =
                    [
                        new DotNetPublishTargetCombination
                        {
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat
                        }
                    ]
                }
            ],
            Bundles = [bundle],
            Steps =
            [
                new DotNetPublishStep
                {
                    Kind = DotNetPublishStepKind.Bundle,
                    BundleId = "package",
                    TargetName = "Sample",
                    Framework = "net10.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    BundleOutputPath = Path.Combine(root, "Artifacts", "package")
                }
            ]
        };
    }

    private static string RunGit(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}
