using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerBootstrap(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        // Planning is portable and never implies permission to apply the generated commands.
        if (!HasOption(subArgs, "--apply"))
            return HandleServerBootstrapPlan(subArgs, outputJson, logger, outputSchemaVersion);
        if (!OperatingSystem.IsLinux() || HasOption(subArgs, "--ssh"))
            throw new InvalidOperationException("Bootstrap --apply runs locally on the Linux target, not through SSH.");
        if (HasOption(subArgs, "--dry-run"))
            throw new InvalidOperationException("Bootstrap cannot combine --apply with --dry-run; omit --apply to plan.");

        var manifestPath = TryGetOptionValue(subArgs, "--manifest") ?? TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(manifestPath))
            return Fail("Missing required --manifest.", outputJson, logger, "web.server.bootstrap");
        var fullPath = Path.GetFullPath(manifestPath);
        var protection = RunLocalServerScript(BuildBootstrapManifestGuard(fullPath));
        if (!protection.Success)
            return Fail("Bootstrap requires root and a root-owned, non-writable regular manifest with protected ancestors.",
                outputJson, logger, "web.server.bootstrap");

        var loaded = LoadServerRecoveryManifest(["--manifest", fullPath], outputJson, logger, "web.server.bootstrap");
        if (loaded.Manifest is null)
            return loaded.ExitCode;
        // Freeze the validated manifest; deployment may update its source repository.
        var script = BuildExecutableBootstrapScript(loaded.Manifest);
        var execution = RunLocalServerScript(script);
        if (!execution.Success)
            return Fail($"Bootstrap stopped with exit {execution.ExitCode}; inspect the host before retrying. No automatic whole-host rollback is implied.",
                outputJson, logger, "web.server.bootstrap");

        return RunServerVerification(loaded.Manifest, fullPath,
            ["--local", "--fail-on-failure", "--url-timeout-seconds", TryGetOptionValue(subArgs, "--url-timeout-seconds") ?? "30"],
            outputJson, logger, outputSchemaVersion, "web.server.bootstrap", reconcileDisabledUnits: true);
    }

    internal static string BuildBootstrapManifestGuard(string manifestPath)
        => string.Join('\n', "set -Eeuo pipefail", "test \"$(id -u)\" = 0",
            BuildRootControlledPathGuardFunction(),
            $"test -f {ShellQuote(manifestPath)} && test ! -L {ShellQuote(manifestPath)}",
            $"powerforge_assert_root_controlled_path {ShellQuote(manifestPath)}");

    internal static string BuildExecutableBootstrapScript(PowerForgeServerRecoveryManifest manifest)
    {
        var steps = BuildBootstrapPlanSteps(manifest, [], includeOperatorVerification: false);
        var unresolved = steps.Where(static step => step.Manual || step.Sensitive ||
            string.IsNullOrWhiteSpace(step.Command) || step.Command.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToArray();
        if (unresolved.Length > 0)
            throw new InvalidOperationException("Bootstrap has unresolved manual/sensitive steps: " +
                string.Join(", ", unresolved.Select(static step => step.Title)) + ". Review bootstrap-plan first.");
        var verification = manifest.Verify;
        if (verification is null ||
            (verification.Commands?.Any(static command => command.Required) != true && !(verification.Urls?.Length > 0)))
            throw new InvalidOperationException("Bootstrap --apply requires at least one required verification command or URL.");
        if (verification.Commands?.Any(static command => command.Sensitive || string.IsNullOrWhiteSpace(command.Command)) == true)
            throw new InvalidOperationException("Bootstrap verification cannot contain skipped sensitive or empty commands.");
        if (verification.Urls?.Any(static url => !Uri.TryCreate(url.Url, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || address.UserInfo.Length > 0 ||
            url.ExpectedStatus is < 100 or > 599) == true)
            throw new InvalidOperationException("Bootstrap verification requires valid HTTP(S) URLs without credentials and valid expected status codes.");
        return RenderBootstrapPlanScript(steps);
    }

    // Stdin avoids predictable/replaceable temporary scripts. Bootstrap does not expose shell output.
    internal static ProcessResult RunLocalServerScript(string script)
    {
        // Read the complete program before evaluating it: an apt/custom command reading stdin
        // must see EOF rather than accidentally consume the remaining bootstrap commands.
        using var process = CreateProcess("/bin/bash", ["-c", "powerforge_program=$(cat); eval \"$powerforge_program\""]);
        process.StartInfo.Environment.Remove("BASH_ENV");
        process.StartInfo.Environment.Remove("ENV");
        process.StartInfo.Environment.Remove("SHELLOPTS");
        process.StartInfo.Environment.Remove("BASHOPTS");
        process.StartInfo.Environment.Remove("CDPATH");
        foreach (var key in process.StartInfo.Environment.Keys.Where(static key => key.StartsWith("BASH_FUNC_", StringComparison.Ordinal)).ToArray())
            process.StartInfo.Environment.Remove(key);
        process.StartInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        var stdout = ReadBoundedServerOutputAsync(process.StandardOutput);
        var stderr = ReadBoundedServerOutputAsync(process.StandardError);
        try
        {
            process.StandardInput.Write(script.Replace("\r\n", "\n", StringComparison.Ordinal));
            process.StandardInput.Close();
            process.WaitForExit();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        return new ProcessResult { ExitCode = process.ExitCode, Stdout = stdout.GetAwaiter().GetResult(), Stderr = stderr.GetAwaiter().GetResult() };
    }

    private static async Task<string> ReadBoundedServerOutputAsync(StreamReader reader)
    {
        const int limit = 65536;
        var output = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            if (output.Length < limit)
                output.Append(buffer, 0, Math.Min(count, limit - output.Length));
        return output.ToString();
    }
}
