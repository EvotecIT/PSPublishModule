namespace PowerForge.Tests;

/// <summary>Acquires SDK-selected runtime inputs before tests inventory an otherwise cold package cache.</summary>
internal static class PowerShellCompilationTestPrerequisites
{
    internal static async Task AcquireRuntimePackAsync(string targetFramework, string runtimeIdentifier)
    {
        // Lock inventories existing assets. Acquire this prerequisite explicitly,
        // retaining the configured ordinary package cache and shared locking path.
        using var workspace = PowerShellCompilationWorkspace.Create(keep: false);
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
            packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_HOME"] = Directory.CreateDirectory(Path.Combine(workspace.Path, "cli-home")).FullName,
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "true",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["NUGET_PACKAGES"] = packageRoot
        };
        var runner = new ProcessRunner(ownProcessTree: true);
        var selected = await runner.RunAsync(PowerShellCompilationSdkProcessEnvironment.CreateInheritedRequest(
            Directory.GetCurrentDirectory(), new[] { "--version" },
            TimeSpan.FromSeconds(30), environment));
        Assert.True(selected.Succeeded, selected.StdErr + selected.StdOut);
        PowerShellCompilationArtifactBuilder.WriteSdkSelection(workspace.Path, selected.StdOut.Trim());
        var project = Path.Combine(workspace.Path, "RuntimePack.csproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{targetFramework}</TargetFramework>
                <RuntimeIdentifier>{runtimeIdentifier}</RuntimeIdentifier>
                <SelfContained>true</SelfContained>
              </PropertyGroup>
            </Project>
            """);
        var restored = await runner.RunAsync(PowerShellCompilationSdkProcessEnvironment.CreateInheritedRequest(
            workspace.Path, new[] { "restore", project, "--nologo", "--verbosity", "minimal" },
            TimeSpan.FromMinutes(5), environment));
        Assert.True(restored.Succeeded, restored.StdErr + restored.StdOut);
    }
}
