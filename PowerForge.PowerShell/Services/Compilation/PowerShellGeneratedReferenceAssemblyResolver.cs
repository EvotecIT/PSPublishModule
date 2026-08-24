using System.Text;

namespace PowerForge;

/// <summary>
/// Materializes target-framework reference assemblies required before typed eligibility analysis.
/// </summary>
internal static class PowerShellGeneratedReferenceAssemblyResolver
{
    private const string Net472ReferencePackageVersion = "1.0.3";
    private static readonly object Net472RestoreLock = new();

    internal static void EnsureAvailable(string? targetFramework)
    {
        var target = targetFramework?.Trim();
        if (target is null || target.Length == 0)
            return;
        if (TryResolve(target))
            return;
        if (!target.Equals("net472", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Reference assemblies for target framework '{target}' could not be located.");

        lock (Net472RestoreLock)
        {
            if (TryResolve(target))
                return;
            RestoreNet472ReferenceAssemblies();
            _ = PowerShellGeneratedTypePolicy.GetReferenceAssemblyPaths(target);
        }
    }

    internal static string GetGeneratedProjectReference(string targetFramework)
        => targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
            ? $"<PackageReference Include=\"Microsoft.NETFramework.ReferenceAssemblies\" Version=\"{Net472ReferencePackageVersion}\" PrivateAssets=\"all\" />"
            : string.Empty;

    private static bool TryResolve(string targetFramework)
    {
        try
        {
            return PowerShellGeneratedTypePolicy.GetReferenceAssemblyPaths(targetFramework).Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void RestoreNet472ReferenceAssemblies()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "PowerForge", "target-framework", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var projectPath = Path.Combine(workspace, "ReferenceAssemblies.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net472</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
                  </ItemGroup>
                </Project>
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                    "dotnet",
                    workspace,
                    new[] { "restore", projectPath, "--nologo", "--verbosity", "minimal" },
                    TimeSpan.FromMinutes(2)))
                .GetAwaiter()
                .GetResult();
            if (run.ExitCode != 0 || run.TimedOut)
            {
                var output = string.IsNullOrWhiteSpace(run.StdErr)
                    ? run.StdOut
                    : run.StdOut + Environment.NewLine + run.StdErr;
                throw new InvalidOperationException(
                    "Unable to restore Microsoft.NETFramework.ReferenceAssemblies for net472 target analysis." +
                    (string.IsNullOrWhiteSpace(output) ? string.Empty : Environment.NewLine + output));
            }
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }
}
