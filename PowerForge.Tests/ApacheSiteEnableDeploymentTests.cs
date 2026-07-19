namespace PowerForge.Tests;

public sealed class ApacheSiteEnableDeploymentTests
{
    [Fact]
    public void Script_ShouldEnableHttpBeforeRequiringCertificateAndHttps()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-apache-site-enable.sh");
        var httpEnable = script.IndexOf("a2ensite \"$http_site\"", StringComparison.Ordinal);
        var certificateGuard = script.IndexOf("if [[ \"$certificate_available\" == 0 ]]", StringComparison.Ordinal);
        var httpsEnable = script.IndexOf("a2ensite \"$https_site\"", StringComparison.Ordinal);

        Assert.True(httpEnable >= 0, "Expected the HTTP site to be enabled.");
        Assert.True(certificateGuard > httpEnable, "Expected certificate validation after HTTP enablement.");
        Assert.True(httpsEnable > certificateGuard, "Expected HTTPS enablement after certificate validation.");
        Assert.Contains("exit 0", script, StringComparison.Ordinal);
        Assert.Contains("HTTP site enabled; obtain or restore certificate", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ShouldValidateInputsAndRollbackFailedApacheChanges()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-apache-site-enable.sh");

        Assert.Contains("must run as root", script, StringComparison.Ordinal);
        Assert.Contains("invalid HTTP site name", script, StringComparison.Ordinal);
        Assert.Contains("invalid HTTPS site name", script, StringComparison.Ordinal);
        Assert.Contains("${#http_site} -le 255", script, StringComparison.Ordinal);
        Assert.Contains("${#https_site} -le 255", script, StringComparison.Ordinal);
        Assert.Contains("invalid certificate name", script, StringComparison.Ordinal);
        Assert.Contains("failed to disable stale HTTPS site", script, StringComparison.Ordinal);
        Assert.Contains("-L \"$https_enabled_path\"", script, StringComparison.Ordinal);
        Assert.Contains("apachectl configtest", script, StringComparison.Ordinal);
        Assert.Contains("restore_site_state", script, StringComparison.Ordinal);
        Assert.Contains("a2dissite \"$site\"", script, StringComparison.Ordinal);
        Assert.Contains("restore_site_state \"$http_site\" \"$http_was_enabled\"", script, StringComparison.Ordinal);
        Assert.Contains("restore_site_state \"$https_site\" \"$https_was_enabled\"", script, StringComparison.Ordinal);
        Assert.Contains("previous site state restored", script, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return File.ReadAllText(Path.Combine([current.FullName, .. relativePath]));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
