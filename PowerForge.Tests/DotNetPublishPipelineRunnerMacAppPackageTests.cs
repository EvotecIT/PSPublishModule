using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerMacAppPackageTests
{
    [Fact]
    public void MacDesktopExample_ValidatesAndDeserializes()
    {
        string root = FindSourceRoot();
        string schemaPath = Path.Combine(root, "Schemas", "powerforge.dotnetpublish.schema.json");
        string examplePath = Path.Combine(root, "Module", "Examples", "DotNetPublish", "Example.MacDesktopApp.json");
        JsonSchema schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        JsonNode document = JsonNode.Parse(File.ReadAllText(examplePath))!;
        EvaluationResults evaluation = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());
        DotNetPublishSpec? spec = JsonSerializer.Deserialize<DotNetPublishSpec>(File.ReadAllText(examplePath), serializerOptions);

        Assert.True(evaluation.IsValid, evaluation.ToString());
        Assert.NotNull(spec);
        DotNetPublishInstaller installer = Assert.Single(spec.Installers);
        Assert.Equal(DotNetPublishInstallerKind.MacApp, installer.Kind);
        Assert.Equal("com.example.sample-studio", installer.MacApp!.BundleIdentifier);
    }

    [Fact]
    public void Plan_AddsMacAppPackageStepAfterPublish()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");

            DotNetPublishPlan plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            DotNetPublishStep step = Assert.Single(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.MacAppPackage);
            DotNetPublishStepKind[] kinds = plan.Steps.Select(candidate => candidate.Kind).ToArray();

            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.MacAppPackage) > Array.IndexOf(kinds, DotNetPublishStepKind.Publish));
            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.Manifest) > Array.IndexOf(kinds, DotNetPublishStepKind.MacAppPackage));
            Assert.DoesNotContain(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.MsiPrepare);
            Assert.Equal("studio.macapp", step.InstallerId);
            Assert.EndsWith("OfficeIMO Studio-0.1.0-osx-arm64.zip", step.InstallerOutputPath, StringComparison.Ordinal);
            Assert.Equal(DotNetPublishInstallerKind.MacApp, Assert.Single(plan.Installers).Kind);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsMacAppForNonMacRuntime()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "linux-x64");

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("osx-*", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("invalid", "BundleIdentifier")]
    [InlineData("com.evotec.officeimo.studio", "../OfficeIMO.Studio")]
    public void Plan_RejectsUnsafeMacMetadata(string bundleIdentifier, string executable)
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");
            DotNetPublishMacAppOptions options = Assert.Single(spec.Installers).MacApp!;
            options.BundleIdentifier = bundleIdentifier;
            options.Executable = executable;

            Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void InfoPlist_ContainsStableIdentityAndDocumentContracts()
    {
        DotNetPublishMacAppOptions options = CreateMacOptions();

        string plist = DotNetPublishPipelineRunner.BuildMacInfoPlist(options, "OfficeIMO.Studio", "AppIcon.icns");

        Assert.Contains("<string>com.evotec.officeimo.studio</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CFBundleShortVersionString</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>0.1.0</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>NSHighResolutionCapable</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>pdf</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>docx</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>AppIcon.icns</string>", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMacAppPackage_OnMac_CreatesSignedInspectableBundleAndZip()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        string root = CreateTempRoot();
        try
        {
            string publish = Directory.CreateDirectory(Path.Combine(root, "publish")).FullName;
            string executable = Path.Combine(publish, "OfficeIMO.Studio");
            File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
            Run("/bin/chmod", root, "0755", executable);
            string package = Path.Combine(root, "artifacts", "OfficeIMO-Studio.zip");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "studio.macapp",
                        Kind = DotNetPublishInstallerKind.MacApp,
                        PrepareFromTarget = "studio",
                        MacApp = CreateMacOptions()
                    }
                }
            };
            var source = new DotNetPublishArtefactResult
            {
                Target = "studio",
                Runtime = "osx-arm64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                OutputDir = publish
            };
            var step = new DotNetPublishStep
            {
                Key = "macapp.package:studio.macapp",
                Kind = DotNetPublishStepKind.MacAppPackage,
                InstallerId = "studio.macapp",
                TargetName = "studio",
                Runtime = "osx-arm64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                InstallerOutputPath = package
            };

            DotNetPublishArtefactResult result = new DotNetPublishPipelineRunner(new NullLogger())
                .BuildMacAppPackage(plan, new[] { source }, step);

            string app = Path.Combine(root, "artifacts", "OfficeIMO Studio.app");
            Assert.True(File.Exists(package));
            Assert.True(File.Exists(Path.Combine(app, "Contents", "Info.plist")));
            Assert.Equal(DotNetPublishArtefactCategory.Installer, result.Category);
            Run("/usr/bin/codesign", root, "--verify", "--deep", "--strict", app);
            string identifier = Run("/usr/libexec/PlistBuddy", root, "-c", "Print :CFBundleIdentifier", Path.Combine(app, "Contents", "Info.plist"));
            Assert.Equal("com.evotec.officeimo.studio", identifier.Trim());
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
                    Id = "studio.macapp",
                    Kind = DotNetPublishInstallerKind.MacApp,
                    PrepareFromTarget = "studio",
                    Runtimes = new[] { runtime },
                    OutputPath = "artifacts",
                    MacApp = CreateMacOptions()
                }
            }
        };
    }

    private static DotNetPublishMacAppOptions CreateMacOptions() => new()
    {
        BundleIdentifier = "com.evotec.officeimo.studio",
        BundleName = "OfficeIMO Studio",
        Version = "0.1.0",
        BuildNumber = "1",
        Executable = "OfficeIMO.Studio",
        MinimumSystemVersion = "13.0",
        Category = "public.app-category.productivity",
        Copyright = "Copyright © Evotec",
        DocumentExtensions = new[] { "pdf", "docx" },
        CodesignIdentity = "-",
        HardenedRuntime = true,
        Timestamp = false
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
        string path = Path.Combine(Path.GetTempPath(), "PowerForgeMacAppTests", Guid.NewGuid().ToString("N"));
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
