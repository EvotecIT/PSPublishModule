using System.Diagnostics;
using PowerForge;

public sealed class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedAssemblyLoadContextBootstrapperParsesInPowerShell7AndWindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var powerShell7 = FindExecutableOnPath("pwsh.exe");
        if (!File.Exists(windowsPowerShell) || powerShell7 is null)
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-template-parse-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(moduleRoot);

        try
        {
            var developmentOptions = new ModuleDevelopmentBinaryBootstrapperOptions(
                ModuleDevelopmentBinaryMode.Environment,
                Path.Combine(root, "Sources", "DemoModule", "bin"),
                "DEMO_USE_DEVELOPMENT_BINARIES",
                "DEMO_DEVELOPMENT_CONFIGURATION",
                new[] { "net10.0", "net8.0" },
                new[] { "net472" });

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                new ExportSet(
                    Array.Empty<string>(),
                    new[] { "Get-Demo" },
                    new[] { "demo" }),
                new[] { "DemoModule.dll" },
                handleRuntimes: true,
                useAssemblyLoadContext: true,
                assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.AllowList,
                assemblyTypeAccelerators: new[] { "Demo.Dependency" },
                ignoreLibrariesOnLoad: new[] { "Ignored.Dependency.dll" },
                developmentBinaries: developmentOptions);

            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            var parserScript = Path.Combine(root, "Test-BootstrapperSyntax.ps1");
            File.WriteAllText(
                parserScript,
                """
param([Parameter(Mandatory = $true)][string] $Path)
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($Path, [ref] $tokens, [ref] $errors) | Out-Null
if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error -Message $_.Message }
    exit 1
}
'BOOTSTRAPPER_PARSE_OK'
""");

            foreach (var host in new[] { powerShell7, windowsPowerShell })
            {
                var result = RunProcess(
                    host,
                    $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{parserScript}\" -Path \"{bootstrapperPath}\"",
                    root,
                    timeoutMilliseconds: 20000);

                Assert.True(
                    result.ExitCode == 0,
                    $"{host} parser proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
                Assert.Contains("BOOTSTRAPPER_PARSE_OK", result.StandardOutput);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopResolverStopsAfterBootstrapAndDoesNotReenterForMissingPowerShellResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-desktop-resolver-" + Guid.NewGuid().ToString("N"));
        var libDefault = Path.Combine(root, "Lib", "Default");
        Directory.CreateDirectory(libDefault);

        try
        {
            var moduleAssembly = BuildDesktopFixture(root);
            File.Copy(
                moduleAssembly,
                Path.Combine(libDefault, "DemoModule.dll"),
                overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false);

            var proofScript = Path.Combine(root, "Validate-ResolverLifetime.ps1");
            File.WriteAllText(
                proofScript,
                """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
$module = Get-Module DemoModule
$resolverState = & $module { $PowerForgeDesktopAssemblyResolverState }
if ($null -eq $resolverState -or
    $resolverState.PSObject.Properties.Name -notcontains 'Registered' -or
    $resolverState.Registered) {
    throw 'The Desktop assembly resolver remained registered after bootstrap.'
}
try {
    Get-Item -LiteralPath (Join-Path $PSScriptRoot 'missing-file') -ErrorAction Stop
} catch [System.Management.Automation.ItemNotFoundException] {
}
'RESOLVER_BOUNDED_OK'
""");

            var result = RunProcess(
                windowsPowerShell,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 20000);

            Assert.True(
                result.ExitCode == 0,
                $"Windows PowerShell resolver proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("RESOLVER_BOUNDED_OK", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopBootstrapperSkipsTopLevelNativeLibrariesWithoutRecordingErrors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-desktop-native-" + Guid.NewGuid().ToString("N"));
        var libDefault = Path.Combine(root, "Lib", "Default");
        Directory.CreateDirectory(libDefault);

        try
        {
            var moduleAssembly = BuildDesktopFixture(root);
            File.Copy(moduleAssembly, Path.Combine(libDefault, "DemoModule.dll"), overwrite: true);
            File.WriteAllBytes(Path.Combine(libDefault, "e_sqlite3.dll"), new byte[] { 0, 1, 2, 3 });

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: true);

            var proofScript = Path.Combine(root, "Validate-NativeLibraryProbe.ps1");
            File.WriteAllText(
                proofScript,
                """
$Error.Clear()
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force -ErrorAction Stop
if ($Error.Count -ne 0) {
    throw "Native library probing recorded $($Error.Count) error(s): $($Error[0].Exception.Message)"
}
'NATIVE_LIBRARY_SKIPPED_CLEANLY'
""");

            var result = RunProcess(
                windowsPowerShell,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 30000);

            Assert.True(
                result.ExitCode == 0,
                $"Windows PowerShell native-library proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("NATIVE_LIBRARY_SKIPPED_CLEANLY", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedCoreBootstrapperPrependsResolvedNativeRuntimeBeforeImport()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var powerShell7 = FindExecutableOnPath("pwsh.exe");
        if (powerShell7 is null)
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-native-runtime-" + Guid.NewGuid().ToString("N"));
        var libCore = Path.Combine(root, "Lib", "Core");
        Directory.CreateDirectory(libCore);

        try
        {
            var moduleAssembly = BuildDesktopFixture(root);
            File.Copy(
                moduleAssembly,
                Path.Combine(libCore, "DemoModule.dll"),
                overwrite: true);

            foreach (var rid in new[] { "win-x64", "win-x86", "win-arm64" })
            {
                Directory.CreateDirectory(Path.Combine(libCore, "runtimes", rid, "native"));
            }

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: true,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var proofScript = Path.Combine(root, "Validate-NativeRuntimeOrder.ps1");
            File.WriteAllText(
                proofScript,
                """
$ErrorActionPreference = 'Stop'
$originalPath = $env:PATH
try {
    Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
    $archFolder = switch ([string][System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        'X64' { 'win-x64' }
        'X86' { 'win-x86' }
        'Arm64' { 'win-arm64' }
        default { throw "Unsupported test architecture: $_" }
    }
    $expected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Lib\Core\runtimes\$archFolder\native"))
    $firstPathEntry = @($env:PATH -split [IO.Path]::PathSeparator)[0]
    if ($firstPathEntry -ne $expected) {
        throw "Native runtime path was not prepended before import. Expected '$expected', got '$firstPathEntry'."
    }
    'NATIVE_RUNTIME_ORDER_OK'
} finally {
    $env:PATH = $originalPath
}
""");

            var result = RunProcess(
                powerShell7,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 30000);

            Assert.True(
                result.ExitCode == 0,
                $"PowerShell Core native runtime proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("NATIVE_RUNTIME_ORDER_OK", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopBootstrapperPreloadsDependenciesFromNestedExportDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "pf-bootstrapper-nested-desktop-dependency-" + Guid.NewGuid().ToString("N"));
        var nestedLib = Path.Combine(root, "Lib", "Plugins", "Deferred");
        Directory.CreateDirectory(nestedLib);

        try
        {
            var fixture = BuildNestedDesktopFixture(root);
            File.Copy(fixture.ModuleAssembly, Path.Combine(nestedLib, "DemoModule.dll"), overwrite: true);
            File.Copy(fixture.DependencyAssembly, Path.Combine(nestedLib, "NestedDependency.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "Plugins/Deferred/DemoModule.dll" },
                handleRuntimes: false);

            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));
            Assert.Contains("'Plugins/Deferred' = @(", libraries, StringComparison.Ordinal);
            Assert.True(
                libraries.IndexOf("NestedDependency.dll", StringComparison.Ordinal) <
                libraries.IndexOf("DemoModule.dll", StringComparison.Ordinal),
                "The nested dependency must be preloaded before its configured export assembly.");

            var proofScript = Path.Combine(root, "Validate-NestedDependency.ps1");
            File.WriteAllText(
                proofScript,
                """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
$module = Get-Module DemoModule
$resolverState = & $module { $PowerForgeDesktopAssemblyResolverState }
if ($resolverState.Registered) { throw 'The Desktop resolver remained registered after bootstrap.' }
if ([DemoModule.Initialize]::Read() -ne 'nested-dependency') { throw 'The deferred nested dependency was unavailable.' }
'NESTED_DEPENDENCY_PRELOADED'
""");

            var result = RunProcess(
                windowsPowerShell,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 30000);

            Assert.True(
                result.ExitCode == 0,
                $"Windows PowerShell nested dependency proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("NESTED_DEPENDENCY_PRELOADED", result.StandardOutput);

            var powerShell7 = FindExecutableOnPath("pwsh.exe");
            if (powerShell7 is not null)
            {
                var coreResult = RunProcess(
                    powerShell7,
                    $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                    root,
                    timeoutMilliseconds: 30000);
                Assert.True(
                    coreResult.ExitCode == 0,
                    $"PowerShell Core nested dependency proof failed.{Environment.NewLine}{coreResult.StandardOutput}{Environment.NewLine}{coreResult.StandardError}");
                Assert.Contains("NESTED_DEPENDENCY_PRELOADED", coreResult.StandardOutput);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MergedDesktopSourceEnforcesAndLoadsRequiredSnapIn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-source-snapin-" + Guid.NewGuid().ToString("N"));
        var libDefault = Directory.CreateDirectory(Path.Combine(root, "Lib", "Default")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(root, "Public")).FullName;

        try
        {
            var moduleAssembly = BuildDesktopFixture(root);
            File.Copy(moduleAssembly, Path.Combine(libDefault, "DemoModule.dll"), overwrite: true);
            File.WriteAllText(
                Path.Combine(publicRoot, "Get-SnapInSource.ps1"),
                "#requires -PSSnapin Microsoft.PowerShell.Core -Version 3.0" + Environment.NewLine +
                "function Get-SnapInSource { (Get-PSSnapin -Name Microsoft.PowerShell.Core).Name }");
            var exports = new ExportSet(new[] { "Get-SnapInSource" }, Array.Empty<string>(), Array.Empty<string>());
            var sources = ModuleMergeComposer.BuildSources(
                root,
                "DemoModule",
                information: null,
                exports,
                fixRelativePaths: false,
                exportAssemblies: new[] { "DemoModule.dll" });

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false);
            var bootstrapperPath = Path.Combine(root, "DemoModule.psm1");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(bootstrapperPath, sources.MergedScriptContent);
            var proofScript = Path.Combine(root, "Validate-SnapInRequirement.ps1");
            File.WriteAllText(
                proofScript,
                """
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DemoModule.psm1') -Force
if ((Get-SnapInSource) -ne 'Microsoft.PowerShell.Core') { throw 'The required snap-in was unavailable to the merged source.' }
'SNAPIN_REQUIREMENT_OK'
""");

            var result = RunProcess(
                windowsPowerShell,
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{proofScript}\"",
                root,
                timeoutMilliseconds: 30000);
            Assert.True(
                result.ExitCode == 0,
                $"Windows PowerShell snap-in requirement proof failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            Assert.Contains("SNAPIN_REQUIREMENT_OK", result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string BuildDesktopFixture(string root)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(root, "Fixture"));
        var projectPath = Path.Combine(projectRoot.FullName, "DemoModule.csproj");
        File.WriteAllText(
            projectPath,
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>DemoModule</AssemblyName>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(
            Path.Combine(projectRoot.FullName, "Initialize.cs"),
            """
namespace DemoModule
{
    public static class Initialize
    {
    }
}
""");

        var result = RunProcess(
            "dotnet",
            $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            projectRoot.FullName,
            timeoutMilliseconds: 60000);
        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed for the Desktop resolver fixture.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        var assemblyPath = Path.Combine(
            projectRoot.FullName,
            "bin",
            "Release",
            "netstandard2.0",
            "DemoModule.dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private static (string ModuleAssembly, string DependencyAssembly) BuildNestedDesktopFixture(string root)
    {
        var fixtureRoot = Directory.CreateDirectory(Path.Combine(root, "NestedFixture"));
        var dependencyRoot = Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "NestedDependency"));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(fixtureRoot.FullName, "DemoModule"));
        var dependencyProject = Path.Combine(dependencyRoot.FullName, "NestedDependency.csproj");
        var moduleProject = Path.Combine(moduleRoot.FullName, "DemoModule.csproj");
        File.WriteAllText(
            dependencyProject,
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>NestedDependency</AssemblyName>
  </PropertyGroup>
</Project>
""");
        File.WriteAllText(
            Path.Combine(dependencyRoot.FullName, "Marker.cs"),
            "namespace NestedDependency { public static class Marker { public static string Value { get { return \"nested-dependency\"; } } } }");
        File.WriteAllText(
            moduleProject,
            $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>DemoModule</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{dependencyProject}" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(
            Path.Combine(moduleRoot.FullName, "Initialize.cs"),
            "namespace DemoModule { public static class Initialize { public static string Read() { return NestedDependency.Marker.Value; } } }");

        var result = RunProcess(
            "dotnet",
            $"build \"{moduleProject}\" -c Release -nologo --verbosity quiet",
            moduleRoot.FullName,
            timeoutMilliseconds: 60000);
        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed for the nested Desktop fixture.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        var outputRoot = Path.Combine(moduleRoot.FullName, "bin", "Release", "netstandard2.0");
        var moduleAssembly = Path.Combine(outputRoot, "DemoModule.dll");
        var dependencyAssembly = Path.Combine(outputRoot, "NestedDependency.dll");
        Assert.True(File.Exists(moduleAssembly), $"Built module assembly not found: {moduleAssembly}");
        Assert.True(File.Exists(dependencyAssembly), $"Built dependency assembly not found: {dependencyAssembly}");
        return (moduleAssembly, dependencyAssembly);
    }

    private static ProcessResult RunProcess(
        string executable,
        string arguments,
        string workingDirectory,
        int timeoutMilliseconds)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException(
                $"Process '{executable}' did not exit within {timeoutMilliseconds} ms.");
        }

        Task.WaitAll(standardOutput, standardError);
        return new ProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue looking for an executable host.
            }
        }

        return null;
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
