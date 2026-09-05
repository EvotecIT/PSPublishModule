using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class CloudflareIncrementalCachePurgeTests
{
    [Theory]
    [InlineData("files", "mode=files")]
    [InlineData("hostname", "mode=hostname")]
    [InlineData("everything", "mode=everything")]
    public void ConfiguredPurgeScript_ShouldExecuteTheEffectiveProfileAsADryRun(string purgeMode, string expectedOutput)
    {
        if (!CommandExists("pwsh")) return;
        var root = NewTempDirectory();
        try
        {
            string siteConfig = Path.Combine(root, "site.json");
            File.WriteAllText(siteConfig, JsonSerializer.Serialize(new
            {
                Name = "Configured purge test",
                BaseUrl = "https://example.test/",
                Cloudflare = new { PurgeMode = purgeMode }
            }));

            var startInfo = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = RepoPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(RepoPath(
                ".github", "actions", "powerforge-cloudflare-site-policy",
                "Invoke-PowerForgeCloudflareConfiguredPurge.ps1"));
            startInfo.Environment["POWERFORGE_CLOUDFLARE_API_TOKEN"] = "test-token";
            startInfo.Environment["POWERFORGE_CLOUDFLARE_CLI_PROJECT"] = RepoPath("PowerForge.Web.Cli", "PowerForge.Web.Cli.csproj");
            startInfo.Environment["POWERFORGE_CLOUDFLARE_DRY_RUN"] = "true";
            startInfo.Environment["POWERFORGE_CLOUDFLARE_HOSTNAME"] = string.Empty;
            startInfo.Environment["POWERFORGE_CLOUDFLARE_SITE_CONFIG"] = siteConfig;
            startInfo.Environment["POWERFORGE_CLOUDFLARE_ZONE_ID"] = ZoneId;
            DotNetTestProcessEnvironment.DisableBuildServers(startInfo);

            using var process = Process.Start(startInfo)!;
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, standardError + Environment.NewLine + standardOutput);
            Assert.Contains(expectedOutput, standardOutput, StringComparison.Ordinal);
            Assert.Contains("Dry run.", standardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
