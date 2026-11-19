using PowerForge;

namespace PowerForge.Tests;

public sealed class DocumentationRenderIsolationTests
{
    [DocumentationIsolationFact]
    [Trait("Category", "Integration")]
    public void DesktopDocumentationCommandsRenderWithIncompatibleOfficeImoInParent()
    {
        var repoRoot = FindDocumentationRepository();
        var configuration = typeof(DocumentationRenderIsolationTests).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var binaries = Path.Combine(repoRoot, "PSPublishModule", "bin", configuration, "net472");
        Assert.True(File.Exists(Path.Combine(binaries, "PSPublishModule.dll")),
            "Build the current PSPublishModule net472 target before this native regression. Missing output: " + binaries);
        var root = Path.Combine(Path.GetTempPath(), "pf-documentation-package-" + Guid.NewGuid().ToString("N"));
        var payload = Directory.CreateDirectory(Path.Combine(root, "Lib", "Default")).FullName;
        try
        {
            foreach (var source in Directory.EnumerateFiles(binaries, "*", SearchOption.AllDirectories))
            {
                if (!source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(source) != ".powerforge-desktop-bindings") continue;
                var target = Path.Combine(payload, Path.GetRelativePath(binaries, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            ModuleBootstrapperGenerator.Generate(root, "PSPublishModule",
                new ExportSet(Array.Empty<string>(), new[] { "Get-ModuleDocumentation", "Show-ModuleDocumentation" }, Array.Empty<string>()),
                new[] { "PSPublishModule.dll" }, handleRuntimes: false,
                ignoreLibrariesOnLoad: new[] { "OfficeIMO.Core.dll", "OfficeIMO.Markdown.dll", "OfficeIMO.Toml.dll", "OfficeIMO.Yaml.dll", "HtmlForgeX.Markdown.dll", "HtmlForgeX.dll" },
                targetFrameworks: new[] { "net472" });
            var scriptPath = Path.Combine(repoRoot, "PowerForge.Tests", "Fixtures", "DocumentationIsolation", "Test-DocumentationIsolation.ps1");
            var newerAssembly = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "Newer")).FullName, "OfficeIMO.Markdown.dll");
            var request = new PowerShellRunRequest(scriptPath, new[]
                { "-ModulePath", Path.Combine(root, "PSPublishModule.psm1"), "-FixturePath", newerAssembly },
                TimeSpan.FromMinutes(3), preferPwsh: false,
                hostRequirement: PowerShellHostRequirement.WindowsPowerShell);
            var result = new PowerShellRunner(new ProcessRunner(ownProcessTree: true)).Run(request);

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("DOCUMENTATION_ISOLATION_OK", result.StdOut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindDocumentationRepository([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        for (var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "PSPublishModule", "PSPublishModule.csproj")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Unable to locate the documentation isolation repository.");
    }

    private sealed class DocumentationIsolationFactAttribute : FactAttribute
    {
        public DocumentationIsolationFactAttribute()
        {
            var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!OperatingSystem.IsWindows() || !File.Exists(host))
                Skip = "The documentation isolation regression requires Windows PowerShell 5.1.";
        }
    }
}
