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
    public void Guardrail_ShouldAcceptExplicitLocalizedAndSingleLanguageModes()
    {
        var localized = CreateSafeSeoDoctorStep();
        localized["requireHreflang"] = true;
        localized["requireHreflangXDefault"] = true;
        var localizedResult = RunGuardrailWithStep(localized);
        Assert.Equal(0, localizedResult.ExitCode);
        Assert.Contains("1 localized", localizedResult.Output, StringComparison.Ordinal);

        var singleLanguageResult = RunGuardrailWithStep(CreateSafeSeoDoctorStep());
        Assert.Equal(0, singleLanguageResult.ExitCode);
        Assert.Contains("1 single-language", singleLanguageResult.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Guardrail_ShouldMatchRunnerAliasesAndCiModeFilters()
    {
        var aliases = CreateSafeSeoDoctorStep();
        aliases["checkContentLeaks"] = "invalid";
        aliases["check-content-leaks"] = true;
        aliases["requireCanonical"] = "invalid";
        aliases["require-canonical"] = true;
        aliases["requireHreflang"] = "invalid";
        aliases["require-hreflang"] = false;
        aliases["requireHreflangXDefault"] = "invalid";
        aliases["require-hreflang-x-default"] = false;
        var aliasResult = RunGuardrailWithStep(aliases);
        Assert.Equal(0, aliasResult.ExitCode);

        var included = CreateSafeSeoDoctorStep();
        included["only-modes"] = new[] { "ci" };
        Assert.Equal(0, RunGuardrailWithStep(included).ExitCode);

        var excluded = CreateSafeSeoDoctorStep();
        excluded["skip-modes"] = new[] { "ci" };
        var excludedResult = RunGuardrailWithStep(excluded);
        Assert.NotEqual(0, excludedResult.ExitCode);
        Assert.Contains("executes in ci mode", excludedResult.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Guardrail_ShouldRejectAmbiguousLocalizationAndEscapingExtends()
    {
        var ambiguous = CreateSafeSeoDoctorStep();
        ambiguous.Remove("requireHreflang");
        ambiguous.Remove("requireHreflangXDefault");
        var ambiguousResult = RunGuardrailWithStep(ambiguous);
        Assert.NotEqual(0, ambiguousResult.ExitCode);
        Assert.Contains("explicit SEO localization mode", ambiguousResult.Output, StringComparison.Ordinal);

        var mismatched = CreateSafeSeoDoctorStep();
        mismatched["requireHreflang"] = true;
        var mismatchedResult = RunGuardrailWithStep(mismatched);
        Assert.NotEqual(0, mismatchedResult.ExitCode);
        Assert.Contains("explicit SEO localization mode", mismatchedResult.Output, StringComparison.Ordinal);

        var escaping = RunGuardrailWithPipeline("{\"extends\":\"../outside.json\"}");
        Assert.NotEqual(0, escaping.ExitCode);
        Assert.Contains("must remain inside", escaping.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guardrail_ShouldRejectBypassedGatesAndRequiredChecks()
    {
        var malformedGate = CreateSafeSeoDoctorStep();
        malformedGate["failOnWarnings"] = "true";
        malformedGate["failOnNewIssues"] = 1;
        var malformedGateResult = RunGuardrailWithStep(malformedGate);
        Assert.NotEqual(0, malformedGateResult.ExitCode);
        Assert.Contains("no resolved ci seo-doctor step gates", malformedGateResult.Output, StringComparison.Ordinal);

        var excludedFromCi = CreateSafeSeoDoctorStep();
        excludedFromCi["modes"] = new[] { "dev" };
        var excludedResult = RunGuardrailWithStep(excludedFromCi);
        Assert.NotEqual(0, excludedResult.ExitCode);
        Assert.Contains("executes in ci mode", excludedResult.Output, StringComparison.Ordinal);

        var canonicalDisabled = CreateSafeSeoDoctorStep();
        canonicalDisabled["checkCanonical"] = false;
        var canonicalResult = RunGuardrailWithStep(canonicalDisabled);
        Assert.NotEqual(0, canonicalResult.ExitCode);
        Assert.Contains("canonical checks enabled", canonicalResult.Output, StringComparison.Ordinal);

        var hreflangDisabled = CreateSafeSeoDoctorStep();
        hreflangDisabled["requireHreflang"] = true;
        hreflangDisabled["requireHreflangXDefault"] = true;
        hreflangDisabled["checkHreflang"] = false;
        var hreflangResult = RunGuardrailWithStep(hreflangDisabled);
        Assert.NotEqual(0, hreflangResult.ExitCode);
        Assert.Contains("checkHreflang to remain enabled", hreflangResult.Output, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> CreateSafeSeoDoctorStep()
        => new(StringComparer.Ordinal)
        {
            ["task"] = "seo-doctor",
            ["failOnWarnings"] = true,
            ["checkContentLeaks"] = true,
            ["requireCanonical"] = true,
            ["requireHreflang"] = false,
            ["requireHreflangXDefault"] = false
        };

    private static (int ExitCode, string Output) RunGuardrailWithStep(Dictionary<string, object?> step)
        => RunGuardrailWithPipeline(JsonSerializer.Serialize(new { steps = new[] { step } }));

    private static (int ExitCode, string Output) RunGuardrailWithPipeline(string pipelineJson)
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-guardrail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pipeline.json"), pipelineJson);
            return RunGuardrail(root, "pipeline.json");
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
