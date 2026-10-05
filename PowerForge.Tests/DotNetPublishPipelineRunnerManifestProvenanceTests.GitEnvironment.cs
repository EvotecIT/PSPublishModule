using System.Diagnostics;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task TrustedGitEnvironment_DisablesAmbientGlobalConfiguration()
    {
        Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("git", out string gitPath),
            "This integration contract requires the build's trusted Git executable.");
        string root = Directory.CreateTempSubdirectory("powerforge-git-global-").FullName;
        try
        {
            string globalConfig = Path.Combine(root, ".gitconfig");
            File.WriteAllText(globalConfig, "[invalid global configuration");
            Dictionary<string, string?> environment =
                DotNetPublishPipelineRunner.CreateTrustedGitEnvironment(
                    new Dictionary<string, string?>
                    {
                        ["HOME"] = root,
                        ["GIT_CONFIG_GLOBAL"] = globalConfig
                    });
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = "config --global --list",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (KeyValuePair<string, string?> variable in environment)
            {
                if (variable.Value is null)
                    process.StartInfo.Environment.Remove(variable.Key);
                else
                    process.StartInfo.Environment[variable.Key] = variable.Value;
            }

            Assert.True(process.Start());
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await stdout);
            Assert.Empty(await stderr);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
