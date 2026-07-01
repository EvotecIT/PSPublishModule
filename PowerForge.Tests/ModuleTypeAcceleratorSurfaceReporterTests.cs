using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class ModuleTypeAcceleratorSurfaceReporterTests
{
    [Fact]
    public void Run_WithEnumTypeAccelerators_AddsReportAndOwnerNote()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            const string assemblyName = "DemoTypes";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "project")).FullName;
            var staging = Path.Combine(tempRoot.FullName, "staging");
            var libCore = Directory.CreateDirectory(Path.Combine(projectRoot, "Lib", "Core")).FullName;
            var fixtureAssembly = BuildTypeFixtureLibrary(tempRoot.FullName, assemblyName);
            File.Copy(fixtureAssembly, Path.Combine(libCore, assemblyName + ".dll"), overwrite: true);
            WriteMinimalManifest(projectRoot, moduleName);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    StagingPath = staging,
                    CsprojPath = Path.Combine(tempRoot.FullName, assemblyName, assemblyName + ".csproj"),
                    Version = "1.0.0",
                    Frameworks = new[] { "net8.0" },
                    ExportAssemblies = new[] { assemblyName + ".dll" },
                    DisableBinaryCmdletScan = true,
                    AssemblyTypeAcceleratorMode = AssemblyTypeAcceleratorExportMode.Enums,
                    AssemblyTypeAcceleratorAssemblies = new[] { assemblyName }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.TypeAcceleratorSurfaceReport);
            Assert.Equal(2, result.TypeAcceleratorSurfaceReport!.TotalRegisteredTypeCount);
            Assert.Contains(result.OwnerNotes, note =>
                string.Equals(note.Title, "Type Accelerators", StringComparison.OrdinalIgnoreCase) &&
                note.Summary.Contains("Enums mode exposes 2 type name(s)", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Artefacts", "Reports", "TypeAccelerators.Core.txt")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteReport_EnumsMode_WritesRegisteredEnumsAndSkippedPublicTypes()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            const string assemblyName = "DemoTypes";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "project")).FullName;
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging")).FullName;
            var libCore = Directory.CreateDirectory(Path.Combine(staging, "Lib", "Core")).FullName;
            var fixtureAssembly = BuildTypeFixtureLibrary(tempRoot.FullName, assemblyName);
            File.Copy(fixtureAssembly, Path.Combine(libCore, assemblyName + ".dll"), overwrite: true);
            WriteMinimalManifest(projectRoot, moduleName);
            WriteMinimalManifest(staging, moduleName);

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    StagingPath = staging,
                    Version = "1.0.0",
                    AssemblyTypeAcceleratorMode = AssemblyTypeAcceleratorExportMode.Enums,
                    AssemblyTypeAcceleratorAssemblies = new[] { assemblyName },
                    AssemblyTypeAccelerators = new[] { "DemoTypes.ColorMode", "DemoTypes.DoesNotExist" }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            });
            var buildResult = new ModuleBuildResult(
                staging,
                Path.Combine(staging, moduleName + ".psd1"),
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
            var reportPath = Path.Combine(projectRoot, "Artefacts", "Reports", "TypeAccelerators.Core.txt");

            var report = new ModuleTypeAcceleratorSurfaceReporter(new NullLogger())
                .WriteReport(plan, buildResult, reportPath);

            Assert.NotNull(report);
            Assert.Equal(AssemblyTypeAcceleratorExportMode.Enums, report.Mode);
            Assert.Equal(2, report!.AssemblyRegisteredTypeCount);
            Assert.Equal(2, report.TotalRegisteredTypeCount);
            Assert.Equal(1, report.SkippedNonEnumTypeCount);
            Assert.Contains("DemoTypes.ColorMode", report.Assemblies.Single().RegisteredTypes);
            Assert.Contains("DemoTypes.ShapeKind", report.Assemblies.Single().RegisteredTypes);
            Assert.DoesNotContain("DemoTypes.Widget", report.Assemblies.Single().RegisteredTypes);
            Assert.Contains("DemoTypes.ColorMode", report.ExplicitTypesFound);
            Assert.Contains("DemoTypes.DoesNotExist", report.ExplicitTypesMissing);
            Assert.True(File.Exists(reportPath), $"Report file was not written: {reportPath}");

            var text = File.ReadAllText(reportPath);
            Assert.Contains("Mode: Enums", text);
            Assert.Contains("Registered accelerator names: 2", text);
            Assert.Contains("Public non-enum types skipped: 1", text);
            Assert.Contains("DemoTypes.ShapeKind", text);
            Assert.Contains("DemoTypes.DoesNotExist", text);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteReport_UsesSameLibraryFolderPreferenceAsBootstrapper()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            const string assemblyName = "DemoTypes";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "project")).FullName;
            var staging = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "staging")).FullName;
            var libCore = Directory.CreateDirectory(Path.Combine(staging, "Lib", "Core")).FullName;
            var libStandard = Directory.CreateDirectory(Path.Combine(staging, "Lib", "Standard")).FullName;
            var coreAssembly = BuildTypeFixtureLibrary(tempRoot.FullName, assemblyName, projectFolderName: "CoreTypes", firstEnumName: "CoreOnlyMode");
            var standardAssembly = BuildTypeFixtureLibrary(tempRoot.FullName, assemblyName, projectFolderName: "StandardTypes", firstEnumName: "StandardOnlyMode");
            File.Copy(coreAssembly, Path.Combine(libCore, assemblyName + ".dll"), overwrite: true);
            File.Copy(standardAssembly, Path.Combine(libStandard, assemblyName + ".dll"), overwrite: true);
            WriteMinimalManifest(projectRoot, moduleName);
            WriteMinimalManifest(staging, moduleName);

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    StagingPath = staging,
                    Version = "1.0.0",
                    AssemblyTypeAcceleratorMode = AssemblyTypeAcceleratorExportMode.Enums,
                    AssemblyTypeAcceleratorAssemblies = new[] { assemblyName }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            });
            var buildResult = new ModuleBuildResult(
                staging,
                Path.Combine(staging, moduleName + ".psd1"),
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
            var reportPath = Path.Combine(projectRoot, "Artefacts", "Reports", "TypeAccelerators.Core.txt");

            var report = new ModuleTypeAcceleratorSurfaceReporter(new NullLogger())
                .WriteReport(plan, buildResult, reportPath);

            Assert.NotNull(report);
            Assert.Contains(Path.Combine("Lib", "Standard"), report!.Assemblies.Single().AssemblyPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DemoTypes.StandardOnlyMode", report.Assemblies.Single().RegisteredTypes);
            Assert.DoesNotContain("DemoTypes.CoreOnlyMode", report.Assemblies.Single().RegisteredTypes);

            var text = File.ReadAllText(reportPath);
            Assert.Contains(Path.Combine("Lib", "Standard"), text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DemoTypes.StandardOnlyMode", text);
            Assert.DoesNotContain("DemoTypes.CoreOnlyMode", text);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildTypeAcceleratorSurfaceOwnerNote_WarnsWhenReportHasMissingExplicitTypes()
    {
        var report = new ModuleTypeAcceleratorSurfaceReport(
            AssemblyTypeAcceleratorExportMode.Enums,
            @"C:\Temp\TypeAccelerators.Core.txt",
            requestedTypes: new[] { "DemoTypes.ColorMode", "DemoTypes.Missing" },
            requestedAssemblies: new[] { "DemoTypes" },
            assemblies: new[]
            {
                new ModuleTypeAcceleratorAssemblyReport(
                    "DemoTypes",
                    registeredTypes: new[] { "DemoTypes.ColorMode", "DemoTypes.ShapeKind" },
                    exportedTypeCount: 3,
                    skippedNonEnumTypeCount: 1)
            },
            explicitTypesFound: new[] { "DemoTypes.ColorMode" },
            explicitTypesMissing: new[] { "DemoTypes.Missing" });

        var note = ModulePipelineRunner.BuildTypeAcceleratorSurfaceOwnerNote(report);

        Assert.Equal(ModuleOwnerNoteSeverity.Warning, note.Severity);
        Assert.Contains("Enums mode exposes 2 type name(s)", note.Summary);
        Assert.Contains("PowerShell type accelerators are process-wide", note.WhyItMatters);
        Assert.Contains(note.Details, detail => detail.Contains("Report:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(note.Details, detail => detail.Contains("1 explicit type(s) missing", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTypeFixtureLibrary(
        string rootPath,
        string assemblyName,
        string? projectFolderName = null,
        string firstEnumName = "ColorMode")
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(rootPath, projectFolderName ?? assemblyName));
        var projectPath = Path.Combine(projectRoot.FullName, assemblyName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Types.cs");

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(sourcePath, $$"""
namespace DemoTypes;

public enum {{firstEnumName}}
{
    Rgb,
    Cmyk
}

public enum ShapeKind
{
    Circle,
    Square
}

public sealed class Widget
{
}
""");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private static void WriteMinimalManifest(string rootPath, string moduleName)
    {
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psd1"), string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            "    ModuleVersion = '1.0.0'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine);
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psm1"), string.Empty);
    }
}
