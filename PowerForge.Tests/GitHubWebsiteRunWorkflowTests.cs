namespace PowerForge.Tests;

public sealed class GitHubWebsiteRunWorkflowTests
{
    [Theory]
    [InlineData("powerforge-website-deploy.yml")]
    [InlineData("powerforge-website-maintenance.yml")]
    public void WebsiteWorkflows_ShouldAllowGitHubPackagesRestore(string workflowFileName)
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", workflowFileName);

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("packages: read", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteCiWorkflow_ShouldSeparateUntrustedAndTrustedCredentialBoundaries()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-ci.yml");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(workflowYaml));
        Assert.Contains("website-ci-untrusted:", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("if: ${{ github.event_name == 'pull_request' || github.event_name == 'pull_request_target' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("credential_mode: none", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("website-ci-trusted:", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("if: ${{ github.event_name != 'pull_request' && github.event_name != 'pull_request_target' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("packages: read", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("credential_mode: standard", workflowYaml, StringComparison.Ordinal);

        string untrustedJob = workflowYaml[
            workflowYaml.IndexOf("  website-ci-untrusted:", StringComparison.Ordinal)..
            workflowYaml.IndexOf("  website-ci-trusted:", StringComparison.Ordinal)];
        Assert.DoesNotContain("packages: read", untrustedJob, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets:", untrustedJob, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteCiWorkflow_ShouldAllowArtifactInspection()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-ci.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("actions: read", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldInheritCallerPermissions()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.DoesNotContain("permissions:", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldExposeCredentialsOnlyInStandardMode()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(workflowYaml));
        Assert.Contains("credential_mode:", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: ${{ inputs.credential_mode != 'none' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Equal(
            2,
            workflowYaml.Split('\n').Count(static line =>
                line.Trim().Equals(
                    "persist-credentials: ${{ inputs.credential_mode != 'none' }}",
                    StringComparison.Ordinal)));
        Assert.Contains("GITHUB_TOKEN: ${{ inputs.credential_mode != 'none' && github.token || '' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("GITHUB_PACKAGES_TOKEN: ${{ inputs.credential_mode != 'none' && (secrets.repository_read_token || github.token) || '' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("LICENSING_PACKAGES_TOKEN: ${{ inputs.credential_mode != 'none' && (secrets.repository_read_token || github.token) || '' }}", workflowYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_ENV", workflowYaml, StringComparison.Ordinal);

        string pipelineStep = workflowYaml[
            workflowYaml.IndexOf("      - name: Run website pipeline", StringComparison.Ordinal)..
            workflowYaml.IndexOf("      - name: Resolve actual PowerForge engine provenance", StringComparison.Ordinal)];
        Assert.All(
            pipelineStep.Split('\n').Where(static line => line.Contains("TOKEN:", StringComparison.Ordinal)),
            static line => Assert.Contains("inputs.credential_mode != 'none'", line, StringComparison.Ordinal));
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldRestoreRunnerWithItsOwnNuGetConfiguration()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");
        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("Join-Path $env:GITHUB_WORKSPACE '.powerforge-runner/.github/nuget.public.config'", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("-p:RestoreConfigFile=$runnerRestoreConfig", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("PowerForge runner NuGet configuration not found", workflowYaml, StringComparison.Ordinal);

        var restoreConfigPath = Path.Combine(repoRoot, ".github", "nuget.public.config");
        var restoreConfig = File.ReadAllText(restoreConfigPath);
        Assert.Contains("<clear />", restoreConfig, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", restoreConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldValidateCredentialModeBeforeCheckout()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");
        var workflowYaml = File.ReadAllText(workflowPath);

        int validationOffset = workflowYaml.IndexOf("      - name: Validate credential mode", StringComparison.Ordinal);
        int checkoutOffset = workflowYaml.IndexOf("      - name: Checkout website", StringComparison.Ordinal);

        Assert.True(validationOffset >= 0);
        Assert.True(checkoutOffset > validationOffset);
        Assert.Contains("-notin @('none', 'standard')", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldInstallRequestedDotNetWorkloads()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("dotnet_workloads:", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("if: ${{ inputs.dotnet_workloads != '' }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("DOTNET_WORKLOADS: ${{ inputs.dotnet_workloads }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("dotnet workload install @workloads --skip-manifest-update", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("Invalid .NET workload identifier", workflowYaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("powerforge-website-ci.yml", 2)]
    [InlineData("powerforge-website-deploy.yml", 2)]
    [InlineData("powerforge-website-maintenance.yml", 1)]
    public void WebsiteWorkflows_ShouldForwardRequestedDotNetWorkloads(string workflowFileName, int expectedForwardCount)
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", workflowFileName);

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("dotnet_workloads:", workflowYaml, StringComparison.Ordinal);
        Assert.Equal(
            expectedForwardCount,
            workflowYaml.Split('\n').Count(static line =>
                line.Trim().Equals("dotnet_workloads: ${{ inputs.dotnet_workloads }}", StringComparison.Ordinal)));
    }

    [Fact]
    public void WebsiteDeployWorkflow_ShouldRetryAStalledGitHubPagesDeployment()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-deploy.yml");
        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(workflowYaml));

        string pagesDeployJob = workflowYaml[
            workflowYaml.IndexOf("  deploy:", StringComparison.Ordinal)..
            workflowYaml.IndexOf("  deploy-linux:", StringComparison.Ordinal)];
        string initialDeployStep = pagesDeployJob[
            pagesDeployJob.IndexOf("      - name: Deploy to GitHub Pages", StringComparison.Ordinal)..
            pagesDeployJob.IndexOf("      - name: Retry GitHub Pages deployment", StringComparison.Ordinal)];
        string retryDeployStep = pagesDeployJob[
            pagesDeployJob.IndexOf("      - name: Retry GitHub Pages deployment", StringComparison.Ordinal)..];

        Assert.Contains("timeout-minutes: ${{ inputs.timeout_minutes }}", pagesDeployJob, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", initialDeployStep, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error:", retryDeployStep, StringComparison.Ordinal);
        Assert.Equal(
            1,
            pagesDeployJob.Split('\n').Count(static line =>
                line.Trim().Equals("continue-on-error: true", StringComparison.Ordinal)));
        Assert.Contains("if: ${{ steps.deployment.outcome == 'failure' }}", pagesDeployJob, StringComparison.Ordinal);
        Assert.Contains(
            "url: ${{ steps.deployment.outputs.page_url || steps.deployment_retry.outputs.page_url }}",
            pagesDeployJob,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            pagesDeployJob.Split('\n').Count(static line =>
                line.Contains("uses: actions/deploy-pages@", StringComparison.Ordinal)));
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldResolveOptionalCallerSourceRefToExactProvenance()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("source_ref:", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ inputs.source_ref || github.event.pull_request.head.sha || github.sha }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("source_sha: ${{ steps.source_provenance.outputs.sha }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("value: ${{ jobs.website-run.outputs.source_sha }}", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("$sourceSha = (git rev-parse HEAD).Trim().ToLowerInvariant()", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldKeepTransientToolCachesInsideWorkspace()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("POWERFORGE_RUNNER_CACHE_ROOT: ${{ github.workspace }}/.cache/powerforge-runner", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("PLAYWRIGHT_BROWSERS_PATH: ${{ github.workspace }}/.cache/powerforge-runner/ms-playwright", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("DOTNET_BUNDLE_EXTRACT_BASE_DIR: ${{ github.workspace }}/.cache/powerforge-runner/dotnet-bundle", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("BUN_INSTALL_CACHE_DIR: ${{ github.workspace }}/.cache/powerforge-runner/bun-install-cache", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("NPM_CONFIG_CACHE: ${{ github.workspace }}/.cache/powerforge-runner/npm-cache", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldScopeNuGetCacheToTheWebsiteJob()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        var nugetEnvironmentLines = workflowYaml
            .Split('\n')
            .Where(static line => line.Contains("NUGET_PACKAGES:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, nugetEnvironmentLines.Length);
        Assert.All(nugetEnvironmentLines, static line => Assert.StartsWith("          NUGET_PACKAGES:", line, StringComparison.Ordinal));
        Assert.Contains("path: ${{ runner.temp }}/powerforge-website-nuget-packages", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("powerforge-website-nuget-v1-${{ runner.os }}-", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("$env:NUGET_PACKAGES", workflowYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("path: ~/.nuget/packages", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldRemoveTransientToolCacheRoot()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");

        Assert.True(File.Exists(workflowPath), $"Website workflow not found: {workflowPath}");

        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("Initialize transient tool cache directories", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("Cleanup transient tool caches before site artifact", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("Cleanup transient tool caches", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("GetFullPath($env:POWERFORGE_RUNNER_CACHE_ROOT)", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("GetFullPath($env:GITHUB_WORKSPACE)", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $cacheRoot -Recurse -Force", workflowYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WebsiteRunWorkflow_ShouldNormalizeRepositoryRootReportPaths()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml");
        var workflowYaml = File.ReadAllText(workflowPath);

        Assert.Contains("inputs.website_root == '.' && '_reports/**'", workflowYaml, StringComparison.Ordinal);
        Assert.Contains("inputs.website_root == '.' && '_site/_reports/**'", workflowYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("./${{ inputs.website_root }}/_reports/**", workflowYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("./${{ inputs.website_root }}/_site/_reports/**", workflowYaml, StringComparison.Ordinal);
        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(workflowYaml));
    }

    [Fact]
    public void WebsiteDeployWorkflow_ShouldCarryExactBuildManifestIntoPostDeploy()
    {
        var repoRoot = FindRepoRoot();
        var runWorkflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-run.yml"));
        var deployWorkflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "powerforge-website-deploy.yml"));

        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(runWorkflow));
        Assert.NotNull(new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(deployWorkflow));
        Assert.Contains("generate_deployment_manifest: ${{ inputs.post_deploy_pipeline_config != '' }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("needs: [build, deploy, cloudflare-site-policy, deploy-linux]", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("source_ref: ${{ needs.build.outputs.source_sha }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment_manifest_artifact_name: ${{ needs.build.outputs.deployment_manifest_artifact_name }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment_base_url: ${{ inputs.deployment_target == 'linux' && inputs.deployment_url || '' }}", deployWorkflow, StringComparison.Ordinal);
        var manifestEnvironmentLines = runWorkflow
            .Split('\n')
            .Where(static line => line.Contains("POWERFORGE_DEPLOYMENT_MANIFEST:", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, manifestEnvironmentLines.Length);
        Assert.All(manifestEnvironmentLines, static line =>
            Assert.StartsWith("          POWERFORGE_DEPLOYMENT_MANIFEST: ${{ runner.temp }}/powerforge-deployment-manifest/manifest.json", line, StringComparison.Ordinal));
        Assert.DoesNotContain("\n      POWERFORGE_DEPLOYMENT_MANIFEST:", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_DEPLOYMENT_BASE_URL: ${{ inputs.deployment_base_url }}", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("name: ${{ inputs.deployment_manifest_artifact_name }}", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("--artifact $env:POWERFORGE_SITE_ARTIFACT --out $env:POWERFORGE_DEPLOYMENT_MANIFEST", runWorkflow, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
            if (File.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for GitHub website workflow tests.");
    }
}
