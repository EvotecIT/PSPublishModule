using System.Diagnostics;
using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task CoreOnlyScriptHelper_PreservesDesktopFunctionsAndLoadsHelperOnCore(bool useAssemblyLoadContext)
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "pf-script-core-helper-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;
        foreach (var folder in new[] { "Private", "Classes", "Enums" })
            Directory.CreateDirectory(Path.Combine(moduleRoot, folder));
        try
        {
            var helper = BuildFixtureProject(root, "Helper", "DemoModule",
                "namespace DemoModule; public static class Initialize { public static string Read() => \"core-helper\"; }");
            File.Copy(helper, Path.Combine(libRoot, "DemoModule.dll"));
            var nativeRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "runtimes", "win-x64", "native")).FullName;
            File.WriteAllText(Path.Combine(nativeRoot, "example.dll"), "native asset, not a Desktop managed helper");
            File.WriteAllText(Path.Combine(publicRoot, "Get-HelperStatus.ps1"), """
function Get-HelperStatus {
    if ($PSEdition -eq 'Core') { [DemoModule.Initialize]::Read() } else { 'script-only' }
}
""");
            ModuleBootstrapperGenerator.Generate(moduleRoot, "DemoModule",
                new ExportSet(new[] { "Get-HelperStatus" }, Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" }, handleRuntimes: true,
                useAssemblyLoadContext: useAssemblyLoadContext, targetFrameworks: new[] { "net8.0" });

            var modulePath = Path.Combine(moduleRoot, "DemoModule.psm1").Replace("'", "''", StringComparison.Ordinal);
            var check = """
$ErrorActionPreference = 'Stop'
Import-Module -Name '{{MODULE}}' -Force -ErrorAction Stop
$expected = if ($PSEdition -eq 'Core') { 'core-helper' } else { 'script-only' }
if ((Get-HelperStatus) -ne $expected) { throw 'Script command returned the wrong result' }
$loaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'DemoModule' }).Count -gt 0
if ($loaded -ne ($PSEdition -eq 'Core')) { throw 'Helper was loaded in the wrong PowerShell edition' }
if ($Error.Count -gt 0) { throw ($Error | Out-String) }
""".Replace("{{MODULE}}", modulePath, StringComparison.Ordinal);
            var windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var executable in new[] { windowsPowerShell, "pwsh" })
            {
                var start = new ProcessStartInfo(executable)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(check)) })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30_000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    Assert.Fail($"Module import timed out in {executable}");
                }
                Assert.True(process.ExitCode == 0,
                    $"Module import failed in {executable}: {await stdout} {await stderr}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
