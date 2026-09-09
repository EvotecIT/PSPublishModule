using System.Reflection;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBootstrapperExportBridgeScopeTests
{
    [Fact]
    public void GeneratedExportBridge_ExportsFromWrapperButDoesNotMutateDotSourcingCaller()
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            string bridge = BuildExportBridge();
            string bridgePayload =
                "$PowerForgeTestInnerModule = Import-Module -Name Microsoft.PowerShell.Management " +
                "-PassThru -Force -ErrorAction Stop" + Environment.NewLine + bridge;
            const string exportedCommand = "Get-Item";

            string wrapperPath = Path.Combine(root.FullName, "WrapperModule.psm1");
            File.WriteAllText(wrapperPath, bridgePayload);
            string wrapperManifestPath = Path.Combine(root.FullName, "WrapperModule.psd1");
            File.WriteAllText(
                wrapperManifestPath,
                "@{ RootModule = 'WrapperModule.psm1'; ModuleVersion = '1.0.0'; " +
                "CmdletsToExport = @('*'); FunctionsToExport = @(); AliasesToExport = @() }");

            string standalonePath = Path.Combine(root.FullName, "Standalone.ps1");
            File.WriteAllText(standalonePath, bridgePayload);
            string callerPath = Path.Combine(root.FullName, "CallerModule.psm1");
            File.WriteAllText(
                callerPath,
                ". '" + EscapePowerShellLiteral(standalonePath) + "'" + Environment.NewLine +
                "function Get-CallerOnly { 'caller' }" + Environment.NewLine +
                "Export-ModuleMember -Function Get-CallerOnly" + Environment.NewLine);

            foreach (string executable in ResolvePowerShellExecutables())
            {
                Assert.True(IsCmdletExported(executable, wrapperPath, "WrapperModule", exportedCommand));
                Assert.True(IsCmdletExported(executable, wrapperManifestPath, "WrapperModule", exportedCommand));
                Assert.False(IsCmdletExported(executable, callerPath, "CallerModule", exportedCommand));
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void GeneratedExportBridge_DerivesCaseComparisonFromContainingDirectory()
    {
        string bridge = BuildExportBridge();

        Assert.DoesNotContain("OSVersion.Platform", bridge, StringComparison.Ordinal);
        Assert.Contains("EnumerateFileSystemEntries", bridge, StringComparison.Ordinal);
        Assert.Contains("StringComparer]::Ordinal", bridge, StringComparison.Ordinal);
    }

    private static string BuildExportBridge()
    {
        MethodInfo? method = typeof(ModuleBootstrapperGenerator).GetMethod(
            "BuildPowerShellModuleExportBridge",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(
            null,
            new object?[] { "$PowerForgeTestInnerModule", "ScopeFixture", null }));
    }

    private static bool IsCmdletExported(
        string executable,
        string modulePath,
        string moduleName,
        string commandName)
    {
        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add("-NoLogo");
        processStartInfo.ArgumentList.Add("-NoProfile");
        processStartInfo.ArgumentList.Add("-NonInteractive");
        processStartInfo.ArgumentList.Add("-ExecutionPolicy");
        processStartInfo.ArgumentList.Add("Bypass");
        processStartInfo.ArgumentList.Add("-Command");
        processStartInfo.ArgumentList.Add(
            "Import-Module -Name '" + EscapePowerShellLiteral(modulePath) +
            "' -Force -ErrorAction Stop; (Get-Module -Name '" + EscapePowerShellLiteral(moduleName) +
            "').ExportedCmdlets.ContainsKey('" + EscapePowerShellLiteral(commandName) + "')");

        using var process = System.Diagnostics.Process.Start(processStartInfo)!;
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Module import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        string result = standardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Last();
        return bool.Parse(result);
    }

    private static string[] ResolvePowerShellExecutables() =>
        OperatingSystem.IsWindows()
            ? new[] { "pwsh", "powershell.exe" }
            : new[] { "pwsh" };

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
