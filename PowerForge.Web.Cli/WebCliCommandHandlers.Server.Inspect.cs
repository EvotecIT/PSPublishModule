using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerInspect(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.inspect");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var failOnDrift = HasOption(subArgs, "--fail-on-drift");
        var target = BuildServerSshTarget(manifest.Target);
        var checks = new List<PowerForgeServerInspectCheck>();
        var warnings = new List<string>();

        AddCommandCheck(checks, "ssh.connect", "connectivity", "SSH target is reachable",
            ExecuteRemote(sshCommand, target, "printf ok"), "ok");

        var os = ExecuteRemote(sshCommand, target, "cat /etc/os-release");
        if (os.Success && !string.IsNullOrWhiteSpace(manifest.Target?.Os))
        {
            var expectedVersion = manifest.Target.Os.Replace("ubuntu-", string.Empty, StringComparison.OrdinalIgnoreCase);
            AddBooleanCheck(checks, "os.version", "os", "OS version matches manifest",
                os.Stdout.Contains(expectedVersion, StringComparison.OrdinalIgnoreCase),
                manifest.Target.Os,
                FirstMatchingLine(os.Stdout, "VERSION_ID") ?? "unknown");
        }
        else
        {
            AddCommandCheck(checks, "os.release", "os", "OS release can be read", os, "success");
        }

        var sshd = ExecuteRemote(sshCommand, target, "sudo -n /usr/sbin/sshd -T 2>/dev/null | awk '/^(port|passwordauthentication|permitrootlogin|x11forwarding|maxauthtries|logingracetime) / { print }'");
        if (sshd.Success && manifest.Target?.SshPort is not null)
        {
            AddBooleanCheck(checks, "ssh.port", "ssh", "SSH port matches manifest",
                HasLine(sshd.Stdout, $"port {manifest.Target.SshPort.Value}"),
                manifest.Target.SshPort.Value.ToString(),
                FirstMatchingLine(sshd.Stdout, "port ") ?? "unknown");
            AddBooleanCheck(checks, "ssh.root-login", "ssh", "Root SSH login is disabled",
                HasLine(sshd.Stdout, "permitrootlogin no"),
                "permitrootlogin no",
                FirstMatchingLine(sshd.Stdout, "permitrootlogin") ?? "unknown");
        }
        else
        {
            AddCommandCheck(checks, "ssh.effective-config", "ssh", "SSH effective config can be read", sshd, "success");
        }

        var packages = ExecuteRemote(sshCommand, target, "dpkg-query -W -f='${binary:Package}\\n'");
        foreach (var package in manifest.Packages?.Apt ?? Array.Empty<string>())
        {
            AddBooleanCheck(checks, $"package.{package}", "packages", $"Package is installed: {package}",
                HasExactLine(packages.Stdout, package),
                package,
                packages.Success ? "installed/missing from dpkg-query" : packages.Stderr.Trim());
        }

        var apacheModules = ExecuteRemote(sshCommand, target, "apache2ctl -M 2>/dev/null || apachectl -M 2>/dev/null");
        foreach (var module in manifest.Packages?.ApacheModules ?? manifest.Apache?.Modules ?? Array.Empty<string>())
        {
            AddBooleanCheck(checks, $"apache.module.{module}", "apache", $"Apache module is enabled: {module}",
                apacheModules.Stdout.Contains($"{module}_module", StringComparison.OrdinalIgnoreCase),
                $"{module}_module",
                apacheModules.Success ? "enabled/missing from apache -M" : apacheModules.Stderr.Trim());
        }

        AddCommandCheck(checks, "apache.configtest", "apache", "Apache configtest succeeds",
            ExecuteRemote(sshCommand, target, manifest.Apache?.ValidateCommand ?? "apachectl configtest"), "Syntax OK");

        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Path)) continue;
            var test = path.Kind?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true
                ? $"test -d {ShellQuote(path.Path)}"
                : $"test -e {ShellQuote(path.Path)}";
            AddBooleanCheck(checks, $"path.{path.Id ?? path.Path}", "paths", $"Managed path exists: {path.Path}",
                ExecuteRemote(sshCommand, target, test).Success,
                path.Kind ?? "exists",
                path.Path);
        }

        InspectSystemdUnits(sshCommand, target, manifest.Systemd?.Services, "service", checks);
        InspectSystemdUnits(sshCommand, target, manifest.Systemd?.Timers, "timer", checks);

        var ufw = ExecuteRemote(sshCommand, target, "sudo -n ufw status numbered");
        AddBooleanCheck(checks, "ufw.active", "firewall", "UFW is active",
            ufw.Stdout.Contains("Status: active", StringComparison.OrdinalIgnoreCase),
            "active",
            FirstMatchingLine(ufw.Stdout, "Status:") ?? ufw.Stderr.Trim());
        foreach (var port in manifest.Firewall?.SshPorts ?? Array.Empty<int>())
        {
            AddBooleanCheck(checks, $"ufw.ssh.{port}", "firewall", $"UFW allows SSH port {port}",
                ufw.Stdout.Contains($"{port}/tcp", StringComparison.OrdinalIgnoreCase),
                $"{port}/tcp allowed",
                "ufw status numbered");
        }
        if (manifest.Firewall?.WebOriginPolicy?.Equals("cloudflare-only", StringComparison.OrdinalIgnoreCase) == true)
        {
            AddBooleanCheck(checks, "ufw.cloudflare-only", "firewall", "HTTP/HTTPS are not open to Anywhere",
                !ufw.Stdout.Contains("80/tcp                     ALLOW IN    Anywhere", StringComparison.OrdinalIgnoreCase) &&
                !ufw.Stdout.Contains("443/tcp                    ALLOW IN    Anywhere", StringComparison.OrdinalIgnoreCase),
                "Cloudflare ranges only",
                "ufw status numbered");
        }

        foreach (var cert in manifest.Certificates ?? Array.Empty<PowerForgeServerCertificate>())
        {
            if (!string.IsNullOrWhiteSpace(cert.RenewalConfigPath))
            {
                AddBooleanCheck(checks, $"cert.{cert.Name}.renewal", "certbot", $"Certbot renewal config exists: {cert.Name}",
                    ExecuteRemote(sshCommand, target, $"test -f {ShellQuote(cert.RenewalConfigPath)}").Success,
                    cert.RenewalConfigPath,
                    cert.RenewalConfigPath);
            }
        }

        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
        {
            if (string.IsNullOrWhiteSpace(secret.Path)) continue;
            var command = secret.RestoreMode?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true
                ? $"test -d {ShellQuote(secret.Path)}"
                : $"test -e {ShellQuote(secret.Path)}";
            AddBooleanCheck(checks, $"secret.{secret.Id}.exists", "secrets", $"Required secret path exists: {secret.Id}",
                ExecuteRemote(sshCommand, target, "sudo -n " + command).Success,
                secret.Path,
                "exists/missing");
        }

        AddCommandCheck(checks, "release.links", "deploy", "Current release symlinks resolve",
            ExecuteRemote(sshCommand, target, "readlink -f /var/www/evotec/xyz/current && readlink -f /var/www/evotec/pl/current"), "/var/www/evotec");

        var failed = checks.Where(static check => !check.Success).ToArray();
        if (failed.Length > 0)
            warnings.Add($"{failed.Length} inspect check(s) reported drift or failure.");

        var result = new PowerForgeServerInspectResult
        {
            ManifestPath = manifestPath,
            Target = target,
            Success = failed.Length == 0,
            Checks = checks.ToArray(),
            Warnings = warnings.ToArray()
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.inspect",
                Success = result.Success || !failOnDrift,
                ExitCode = result.Success || !failOnDrift ? 0 : 1,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerInspectResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return result.Success || !failOnDrift ? 0 : 1;
        }

        logger.Success(result.Success ? "Server inspect completed without drift." : "Server inspect completed with drift.");
        logger.Info($"Target: {target}");
        logger.Info($"Checks: {checks.Count}; failures: {failed.Length}");
        foreach (var failure in failed.Take(20))
            logger.Warn($"{failure.Id}: {failure.Message} (expected: {failure.Expected}; actual: {failure.Actual})");
        if (failed.Length > 20)
            logger.Warn($"Additional failures omitted: {failed.Length - 20}");

        return result.Success || !failOnDrift ? 0 : 1;
    }

    private static ProcessResult ExecuteRemote(string sshCommand, string target, string command)
        => RunProcessCaptureText(sshCommand, new[] { target, $"sh -lc {ShellQuote(command)}" });

    private static void InspectSystemdUnits(
        string sshCommand,
        string target,
        PowerForgeServerSystemdUnit[]? units,
        string kind,
        ICollection<PowerForgeServerInspectCheck> checks)
    {
        foreach (var unit in units ?? Array.Empty<PowerForgeServerSystemdUnit>())
        {
            if (string.IsNullOrWhiteSpace(unit.Name)) continue;
            var exists = ExecuteRemote(sshCommand, target, $"systemctl cat {ShellQuote(unit.Name)} >/dev/null");
            AddBooleanCheck(checks, $"systemd.{kind}.{unit.Name}.exists", "systemd", $"systemd {kind} exists: {unit.Name}",
                exists.Success,
                "unit exists",
                exists.Success ? "unit exists" : exists.Stderr.Trim());

            if (!unit.Enabled) continue;
            var enabled = ExecuteRemote(sshCommand, target, $"systemctl is-enabled {ShellQuote(unit.Name)}");
            AddBooleanCheck(checks, $"systemd.{kind}.{unit.Name}.enabled", "systemd", $"systemd {kind} is enabled: {unit.Name}",
                enabled.Stdout.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase),
                "enabled",
                enabled.Stdout.Trim());
        }
    }

    private static void AddCommandCheck(
        ICollection<PowerForgeServerInspectCheck> checks,
        string id,
        string category,
        string message,
        ProcessResult result,
        string expected)
    {
        checks.Add(new PowerForgeServerInspectCheck
        {
            Id = id,
            Category = category,
            Message = message,
            Success = result.Success,
            Expected = expected,
            Actual = result.Success ? result.Stdout.Trim() : result.Stderr.Trim()
        });
    }

    private static void AddBooleanCheck(
        ICollection<PowerForgeServerInspectCheck> checks,
        string id,
        string category,
        string message,
        bool success,
        string? expected,
        string? actual)
    {
        checks.Add(new PowerForgeServerInspectCheck
        {
            Id = id,
            Category = category,
            Message = message,
            Success = success,
            Expected = expected,
            Actual = actual
        });
    }

    private static bool HasExactLine(string text, string expected)
        => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static bool HasLine(string text, string expected)
        => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static string? FirstMatchingLine(string text, string prefix)
        => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?.Trim();
}
