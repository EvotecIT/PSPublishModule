namespace PowerForge.Tests;

public sealed class CertbotApacheReloadDeploymentTests
{
    [Fact]
    public void AcmeHttpWebroot_ShouldExposeOnlyTheStableChallengeDirectory()
    {
        var config = ReadRepoFile("Deployment", "Linux", "apache", "powerforge-acme-http-webroot.conf");

        Assert.Contains("Alias /.well-known/acme-challenge/ /var/lib/letsencrypt/.well-known/acme-challenge/", config, StringComparison.Ordinal);
        Assert.Contains("<Directory /var/lib/letsencrypt/.well-known/acme-challenge>", config, StringComparison.Ordinal);
        Assert.Contains("Options None", config, StringComparison.Ordinal);
        Assert.Contains("AllowOverride None", config, StringComparison.Ordinal);
        Assert.Contains("Require all granted", config, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployHook_ShouldValidateApacheBeforeReloading()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-certbot-reload-apache.sh");
        var configTest = script.IndexOf("\"$APACHECTL\" configtest", StringComparison.Ordinal);
        var reload = script.IndexOf("\"$SYSTEMCTL\" reload apache2", StringComparison.Ordinal);

        Assert.True(configTest >= 0, "Expected Apache configuration validation.");
        Assert.True(reload > configTest, "Expected Apache reload only after configuration validation.");
        Assert.Contains("if ! configtest_output=", script, StringComparison.Ordinal);
        Assert.Contains("printf '%s\\n' \"$configtest_output\" >&2", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployHook_ShouldUseAbsoluteExecutableDefaultsAndRunItsLinuxFixtureInCi()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-certbot-reload-apache.sh");
        var workflow = ReadRepoFile(".github", "workflows", "BuildModule.yml");

        Assert.Contains("/usr/sbin/apache2ctl", script, StringComparison.Ordinal);
        Assert.Contains("/usr/bin/systemctl", script, StringComparison.Ordinal);
        Assert.Contains("must be an executable absolute path", script, StringComparison.Ordinal);
        Assert.Contains("bash Deployment/Linux/tests/powerforge-certbot-reload-apache-fixture.sh", workflow, StringComparison.Ordinal);
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
