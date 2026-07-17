using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubWebsiteDeployGuardrailTests
{
    [Fact]
    public void ReusableWorkflow_ShouldInvokeSharedGuardrailWithoutEmbeddedPolicyBrain()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");

        Assert.Contains("Assert-PowerForgeWebsiteDeployGuardrails.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-ResolvedPipelineSteps", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$fullyConfiguredSeoDoctorSteps", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Guardrail_ShouldAcceptExplicitSeoModesAndRejectAmbiguousOrEscapingConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-guardrail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var validPath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(validPath, JsonSerializer.Serialize(new
            {
                steps = new object[]
                {
                    new
                    {
                        task = "seo-doctor",
                        failOnWarnings = true,
                        checkContentLeaks = true,
                        requireCanonical = true,
                        requireHreflang = true,
                        requireHreflangXDefault = true
                    }
                }
            }));

            var accepted = RunGuardrail(root, "pipeline.json");
            Assert.Equal(0, accepted.ExitCode);
            Assert.Contains("Validated 1 seo-doctor", accepted.Output, StringComparison.Ordinal);

            File.WriteAllText(validPath, JsonSerializer.Serialize(new
            {
                steps = new object[]
                {
                    new
                    {
                        task = "seo-doctor",
                        failOnWarnings = true,
                        checkContentLeaks = true,
                        requireCanonical = true,
                        requireHreflang = false,
                        requireHreflangXDefault = false
                    }
                }
            }));
            var singleLanguage = RunGuardrail(root, "pipeline.json");
            Assert.Equal(0, singleLanguage.ExitCode);
            Assert.Contains("1 single-language", singleLanguage.Output, StringComparison.Ordinal);

            File.WriteAllText(validPath, JsonSerializer.Serialize(new
            {
                steps = new object[]
                {
                    new
                    {
                        task = "seo-doctor",
                        failOnWarnings = true,
                        checkContentLeaks = true,
                        requireCanonical = true
                    }
                }
            }));
            var ambiguous = RunGuardrail(root, "pipeline.json");
            Assert.NotEqual(0, ambiguous.ExitCode);
            Assert.Contains("explicit SEO localization mode", ambiguous.Output, StringComparison.Ordinal);

            File.WriteAllText(validPath, JsonSerializer.Serialize(new
            {
                steps = new object[]
                {
                    new
                    {
                        task = "seo-doctor",
                        failOnWarnings = true,
                        checkContentLeaks = true,
                        requireCanonical = true,
                        requireHreflang = true,
                        requireHreflangXDefault = false
                    }
                }
            }));
            var mismatched = RunGuardrail(root, "pipeline.json");
            Assert.NotEqual(0, mismatched.ExitCode);
            Assert.Contains("explicit SEO localization mode", mismatched.Output, StringComparison.Ordinal);

            File.WriteAllText(validPath, "{\"extends\":\"../outside.json\"}");
            var rejected = RunGuardrail(root, "pipeline.json");
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("must remain inside", rejected.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Output) RunGuardrail(string workspace, string pipeline)
    {
        var script = GetRepoPath("Build", "Assert-PowerForgeWebsiteDeployGuardrails.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-DeploymentTarget");
        startInfo.ArgumentList.Add("linux");
        startInfo.ArgumentList.Add("-DeploymentUrl");
        startInfo.ArgumentList.Add("https://example.test");
        startInfo.ArgumentList.Add("-PipelineConfig");
        startInfo.ArgumentList.Add(pipeline);
        startInfo.ArgumentList.Add("-Workspace");
        startInfo.ArgumentList.Add(workspace);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    private static string ReadRepoFile(params string[] relativePath)
        => File.ReadAllText(GetRepoPath(relativePath));

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
