using PowerForge;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [DesktopCoexistenceFact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopModuleCoexistsWithEarlierVersionBlindResolverWithoutBroadeningScopeOrRemovingOtherHandlers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-desktop-coexistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var inputs = Path.Combine(root, "Inputs");
            Directory.CreateDirectory(inputs);
            foreach (var file in Directory.GetFiles(FindDesktopCoexistenceFixtures()))
                File.Copy(file, Path.Combine(inputs, Path.GetFileName(file)));

            var build = RunProcess(DesktopCoexistenceHost(),
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(inputs, "Build-Fixture.ps1")}\" -Root \"{root}\"",
                root, timeoutMilliseconds: 60000);
            Assert.True(build.ExitCode == 0, build.StandardOutput + Environment.NewLine + build.StandardError);
            Assert.Contains("DESKTOP_COEXISTENCE_FIXTURE_BUILT", build.StandardOutput);

            ModuleBootstrapperGenerator.Generate(
                Path.Combine(root, "Module"),
                "PowerForgeDesktopCoexistence",
                new ExportSet(Array.Empty<string>(), new[] { "Get-DesktopCoexistence" }, Array.Empty<string>()),
                new[] { "Default/PowerForgeDesktopCoexistence.dll", "Dependencies/DesktopCoexistence.Dependency.dll" },
                handleRuntimes: false,
                // A separate, declared directory requires Add-Type's GetTypes bind to use AssemblyResolve.
                ignoreLibrariesOnLoad: new[] { "DesktopCoexistence.Dependency.dll" });

            var result = RunProcess(DesktopCoexistenceHost(),
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(inputs, "Validate-Coexistence.ps1")}\" -Root \"{root}\"",
                root, timeoutMilliseconds: 30000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("DESKTOP_COEXISTENCE_IMPORT_OK", result.StandardOutput);
            Assert.Contains("DESKTOP_COEXISTENCE_SCOPE_OK", result.StandardOutput);
            Assert.Contains("DESKTOP_COEXISTENCE_REMOVAL_OK", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string FindDesktopCoexistenceFixtures(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile)! })
        {
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                    return Path.Combine(current.FullName, "PowerForge.Tests", "Fixtures", "DesktopCoexistence");
            }
        }
        throw new DirectoryNotFoundException("Unable to locate the Desktop coexistence fixture sources.");
    }

    private static string DesktopCoexistenceHost() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private sealed class DesktopCoexistenceFactAttribute : FactAttribute
    {
        public DesktopCoexistenceFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(DesktopCoexistenceHost()))
                Skip = "Windows PowerShell 5.1 is unavailable.";
        }
    }
}
