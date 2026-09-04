using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerDebianPackageTests
{
    [Fact]
    public void LinuxDesktopExample_ValidatesAndDeserializes()
    {
        string root = FindSourceRoot();
        string schemaPath = Path.Combine(root, "Schemas", "powerforge.dotnetpublish.schema.json");
        string examplePath = Path.Combine(root, "Module", "Examples", "DotNetPublish", "Example.LinuxDesktopDebian.json");
        JsonSchema schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        JsonNode document = JsonNode.Parse(File.ReadAllText(examplePath))!;
        EvaluationResults evaluation = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());
        DotNetPublishSpec? spec = JsonSerializer.Deserialize<DotNetPublishSpec>(File.ReadAllText(examplePath), serializerOptions);

        Assert.True(evaluation.IsValid, evaluation.ToString());
        Assert.NotNull(spec);
        DotNetPublishInstaller installer = Assert.Single(spec.Installers);
        Assert.Equal(DotNetPublishInstallerKind.Debian, installer.Kind);
        Assert.Equal("sample-studio", installer.Debian!.PackageName);
    }

    [Fact]
    public void Plan_AddsDebianPackageStepAfterPublish()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "linux-x64");

            DotNetPublishPlan plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            DotNetPublishStep step = Assert.Single(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.DebianPackage);
            DotNetPublishStepKind[] kinds = plan.Steps.Select(candidate => candidate.Kind).ToArray();

            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.DebianPackage) > Array.IndexOf(kinds, DotNetPublishStepKind.Publish));
            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.Manifest) > Array.IndexOf(kinds, DotNetPublishStepKind.DebianPackage));
            Assert.DoesNotContain(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.MsiPrepare);
            Assert.Equal("studio.debian", step.InstallerId);
            Assert.EndsWith("officeimo-studio_0.1.0_amd64.deb", step.InstallerOutputPath, StringComparison.Ordinal);
            Assert.Equal(DotNetPublishInstallerKind.Debian, Assert.Single(plan.Installers).Kind);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsDebianPackageForNonLinuxRuntime()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "win-x64");

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("linux-*", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("OfficeIMO-Studio", "PackageName", null)]
    [InlineData("officeimo-studio", "Executable", "../OfficeIMO.Studio")]
    public void Plan_RejectsUnsafeDebianMetadata(string value, string property, string? executable = null)
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "linux-x64");
            DotNetPublishDebianOptions options = Assert.Single(spec.Installers).Debian!;
            if (property == "PackageName")
                options.PackageName = value;
            else
                options.Executable = executable!;

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains(property, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsControlAndDesktopLineInjection()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "linux-x64");
            DotNetPublishDebianOptions options = Assert.Single(spec.Installers).Debian!;
            options.Depends = "libc6\nConflicts: sample";

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("Depends", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("one line", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void MetadataWriters_ProduceDesktopAndDebianContracts()
    {
        DotNetPublishDebianOptions options = CreateDebianOptions();

        string control = DotNetPublishPipelineRunner.BuildDebianControl(options, "amd64");
        string desktop = DotNetPublishPipelineRunner.BuildDesktopEntry(options);

        Assert.Contains("Package: officeimo-studio\n", control, StringComparison.Ordinal);
        Assert.Contains("Version: 0.1.0\n", control, StringComparison.Ordinal);
        Assert.Contains("Architecture: amd64\n", control, StringComparison.Ordinal);
        Assert.Contains("Exec=/usr/bin/officeimo-studio %F\n", desktop, StringComparison.Ordinal);
        Assert.Contains("Icon=officeimo-studio\n", desktop, StringComparison.Ordinal);
        Assert.Contains("Categories=Office;Utility;\n", desktop, StringComparison.Ordinal);
        Assert.Contains("MimeType=application/pdf;\n", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDebianPackage_OnLinux_CreatesInspectablePackage()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        string root = CreateTempRoot();
        try
        {
            string publish = Directory.CreateDirectory(Path.Combine(root, "publish")).FullName;
            File.WriteAllText(Path.Combine(publish, "OfficeIMO.Studio"), "#!/bin/sh\nexit 0\n");
            File.WriteAllBytes(Path.Combine(root, "icon.png"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            string package = Path.Combine(root, "artifacts", "officeimo-studio_0.1.0_amd64.deb");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "studio.debian",
                        Kind = DotNetPublishInstallerKind.Debian,
                        PrepareFromTarget = "studio",
                        Debian = CreateDebianOptions()
                    }
                }
            };
            plan.Installers[0].Debian!.IconPath = "icon.png";
            var source = new DotNetPublishArtefactResult
            {
                Target = "studio",
                Runtime = "linux-x64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                OutputDir = publish
            };
            var step = new DotNetPublishStep
            {
                Key = "debian.package:studio.debian",
                Kind = DotNetPublishStepKind.DebianPackage,
                InstallerId = "studio.debian",
                TargetName = "studio",
                Runtime = "linux-x64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                InstallerOutputPath = package
            };

            DotNetPublishArtefactResult result = new DotNetPublishPipelineRunner(new NullLogger())
                .BuildDebianPackage(plan, new[] { source }, step);

            Assert.True(File.Exists(package));
            Assert.Equal(DotNetPublishArtefactCategory.Installer, result.Category);
            string fields = Run("dpkg-deb", root, "--field", package);
            string contents = Run("dpkg-deb", root, "--contents", package);
            Assert.Contains("Package: officeimo-studio", fields, StringComparison.Ordinal);
            Assert.Contains("Architecture: amd64", fields, StringComparison.Ordinal);
            Assert.Contains("./opt/officeimo-studio/OfficeIMO.Studio", contents, StringComparison.Ordinal);
            Assert.Contains("./usr/share/applications/officeimo-studio.desktop", contents, StringComparison.Ordinal);
            Assert.Contains("./usr/bin/officeimo-studio -> /opt/officeimo-studio/OfficeIMO.Studio", contents, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishSpec CreateSpec(string root, string runtime)
    {
        string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "Studio")).FullName;
        string projectPath = Path.Combine(projectDirectory, "Studio.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return new DotNetPublishSpec
        {
            DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = "studio",
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { runtime },
                        Style = DotNetPublishStyle.PortableCompat
                    }
                }
            },
            Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "studio.debian",
                    Kind = DotNetPublishInstallerKind.Debian,
                    PrepareFromTarget = "studio",
                    Runtimes = new[] { runtime },
                    OutputPath = "artifacts",
                    Debian = CreateDebianOptions()
                }
            }
        };
    }

    private static DotNetPublishDebianOptions CreateDebianOptions() => new()
    {
        PackageName = "officeimo-studio",
        Version = "0.1.0",
        Maintainer = "Evotec <support@evotec.pl>",
        Description = "Cross-platform OfficeIMO document studio.",
        Executable = "OfficeIMO.Studio",
        CommandName = "officeimo-studio",
        InstallDirectoryName = "officeimo-studio",
        DesktopName = "OfficeIMO Studio",
        DesktopComment = "Open and work with documents locally.",
        DesktopCategories = "Office;Utility",
        MimeTypes = "application/pdf",
        StartupWmClass = "OfficeIMO.Studio",
        IconPath = "icon.png"
    };

    private static string Run(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "PowerForgeDebianTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindSourceRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
