namespace PowerForge.Tests;

public sealed class CloudflareOriginSyncDeploymentTests
{
    [Fact]
    public void SyncScript_ShouldValidateCloudflareRangesBeforeTrustingThem()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-cloudflare-origin-sync.sh");

        Assert.Contains("https://www.cloudflare.com/ips-v4", script, StringComparison.Ordinal);
        Assert.Contains("https://www.cloudflare.com/ips-v6", script, StringComparison.Ordinal);
        Assert.Contains("--proto '=https'", script, StringComparison.Ordinal);
        Assert.Contains("--proto-redir '=https'", script, StringComparison.Ordinal);
        Assert.Contains("--max-filesize 65536", script, StringComparison.Ordinal);
        Assert.Contains("ipaddress.ip_network", script, StringComparison.Ordinal);
        Assert.Contains("not network.is_global", script, StringComparison.Ordinal);
        Assert.Contains("contained too few CIDRs", script, StringComparison.Ordinal);
        Assert.Contains("RemoteIPHeader CF-Connecting-IP", script, StringComparison.Ordinal);
        Assert.Contains("RemoteIPTrustedProxy", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncScript_ShouldRefreshFirewallAndRollbackInvalidApacheConfiguration()
    {
        var script = ReadRepoFile("Deployment", "Linux", "powerforge-cloudflare-origin-sync.sh");

        Assert.Contains("flock -n 9", script, StringComparison.Ordinal);
        Assert.Contains("ufw allow proto tcp", script, StringComparison.Ordinal);
        Assert.Contains("ufw --force delete allow", script, StringComparison.Ordinal);
        Assert.Contains("apachectl configtest", script, StringComparison.Ordinal);
        Assert.Contains("restore_apache_configuration", script, StringComparison.Ordinal);
        Assert.Contains("module or configuration enablement failed", script, StringComparison.Ordinal);
        Assert.Contains("systemctl reload", script, StringComparison.Ordinal);
        Assert.Contains("install -m 0644 \"$tmp_dir/cidrs.txt\" \"$state_file\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemdTimer_ShouldUseGenericPowerForgePathsAndPersistentScheduling()
    {
        var service = ReadRepoFile("Deployment", "Linux", "systemd", "powerforge-cloudflare-origin-sync.service");
        var timer = ReadRepoFile("Deployment", "Linux", "systemd", "powerforge-cloudflare-origin-sync.timer");
        var environment = ReadRepoFile("Deployment", "Linux", "powerforge-cloudflare-origin.env.example");

        Assert.Contains("EnvironmentFile=-/etc/powerforge/cloudflare-origin.env", service, StringComparison.Ordinal);
        Assert.Contains("ExecStart=/usr/local/sbin/powerforge-cloudflare-origin-sync", service, StringComparison.Ordinal);
        Assert.Contains("OnCalendar=daily", timer, StringComparison.Ordinal);
        Assert.Contains("Persistent=true", timer, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CLOUDFLARE_MANAGE_UFW=1", environment, StringComparison.Ordinal);
        Assert.DoesNotContain("evotec", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evotec", timer, StringComparison.OrdinalIgnoreCase);
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
