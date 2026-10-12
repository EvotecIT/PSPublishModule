#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private static string Tool(string name) => name switch
    {
        "git" => "/usr/bin/git",
        "gh" => "/opt/homebrew/bin/gh",
        "pwsh" => "/usr/local/bin/pwsh",
        _ => throw new ArgumentException("Worker tool is not fixed.")
    };

    internal static ProcessStartInfo CreateProcess(string executable, string root, IEnumerable<string> arguments, bool apple = false)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment.Clear();
        foreach (var name in new[] { "HOME", "USER", "LOGNAME", "TMPDIR", "SSH_AUTH_SOCK", "GH_TOKEN", "GITHUB_TOKEN", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "POWERFORGE_APPLE_STATE_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) start.Environment[name] = value;
        }
        start.Environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin";
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "en_US.UTF-8";
        if (apple)
            foreach (var name in new[] { "APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_ISSUER_ID", "APP_STORE_CONNECT_PRIVATE_KEY_PATH", "ASC_KEY_ID", "ASC_ISSUER_ID", "ASC_PRIVATE_KEY_PATH" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value)) start.Environment[name] = value;
            }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private string Process(string name, string root, IEnumerable<string> arguments, bool apple = false)
    {
        var start = CreateProcess(Tool(name), root, arguments, apple);
        if (apple) start.Environment["POWERFORGE_APPLE_WORKER_GATE"] = _gate;
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start the trusted local tool.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var stopwatch = Stopwatch.StartNew();
        while (!process.WaitForExit(apple ? 30000 : 1000))
        {
            if (stopwatch.Elapsed > (apple ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(2)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new AppleTestFlightToolException("Local tool exceeded its bounded run. Inspect retained receipts before retrying a mutation.");
            }
            if (apple)
            {
                try { SyncRequests(); UpdateGate(); }
                catch { AppleReleaseAppState.AtomicWrite(_gate, new { Allowed = false, ExpiresAt = DateTimeOffset.UtcNow }); }
            }
        }
        var output = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (output.Length > 16 * 1024 * 1024 || error.Length > 16 * 1024 * 1024)
            throw new InvalidOperationException("Local tool output exceeds the bounded response limit.");
        if (apple && IsPreMutationRejection(process.ExitCode, output))
            throw new AppleTestFlightPreMutationRejectedException();
        if (process.ExitCode != 0 && !apple)
            throw new AppleTestFlightToolException($"Trusted {name} failed with exit code {process.ExitCode}. Inspect local authentication, source, or repository permissions.");
        return output;
    }

    internal static bool IsPreMutationRejection(int exitCode, string output)
    {
        if (exitCode != AppleReleaseLeaseContentionException.ExitCode) return false;
        try
        {
            using var document = JsonDocument.Parse(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim());
            var value = document.RootElement;
            return Text(value, "command") == "apple-release" && !value.GetProperty("success").GetBoolean() &&
                value.GetProperty("exitCode").GetInt32() == exitCode && Text(value, "errorCode") is "AppleAppLeaseBusy" or "AppleMutationGateClosed";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    private string Git(params string[] arguments) => GitAt(_policy.ConsumerRoot, arguments);
    private string GitAt(string root, params string[] arguments) => Process("git", root,
        new[] { "-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false", "-c", "core.quotePath=false", "-C", root }.Concat(arguments)).Trim();

    private JsonElement Api(string path, params string[] arguments)
    {
        var endpoint = string.IsNullOrEmpty(path) ? $"repos/{_policy.Repository}" : $"repos/{_policy.Repository}/{path}";
        var json = Process("gh", _policy.OwnerRoot, new[] { "api", "--hostname", "github.com", endpoint }.Concat(arguments));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private IEnumerable<JsonElement> Pages(string path, string? arrayName = null)
    {
        for (var page = 1; page <= 100; page++)
        {
            var response = Api(path + (path.Contains('?') ? "&" : "?") + $"per_page=100&page={page}");
            var values = (arrayName is null ? response : response.GetProperty(arrayName)).EnumerateArray().Select(value => value.Clone()).ToArray();
            foreach (var value in values) yield return value;
            if (values.Length < 100) yield break;
        }
        throw new InvalidOperationException("GitHub pagination exceeded the worker bound; narrow enrollment before continuing.");
    }

    private void AssertOwner()
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("Apple mutation worker runs only on the trusted local Mac.");
        if (GitAt(_policy.OwnerRoot, "rev-parse", "HEAD") != _policy.OwnerCommit || GitAt(_policy.OwnerRoot, "status", "--porcelain=v1", "--untracked-files=all").Length != 0)
            throw new InvalidOperationException("Worker owner checkout must be clean at its exact configured PowerForge pin.");
        AssertOrigin(_policy.OwnerRoot, "EvotecIT/PSPublishModule");
        AssertOrigin(_policy.ConsumerRoot, _policy.Repository);
        if (Git("symbolic-ref", "--short", "HEAD") != "main" || Git("for-each-ref", "--format=%(refname)", "refs/replace").Length != 0)
            throw new InvalidOperationException("Worker checkout must be main without replacement refs.");
        if (Text(Api(""), "default_branch") != "main") throw new InvalidOperationException("Worker supports the main default branch only.");
    }

    private void AssertOrigin(string root, string repository)
    {
        var origin = GitAt(root, "remote", "get-url", "origin");
        if (origin != $"https://github.com/{repository}.git" && origin != $"https://github.com/{repository}" && origin != $"git@github.com:{repository}.git")
            throw new InvalidOperationException("Checkout origin does not match the enrolled GitHub repository.");
    }

    private PowerForgeAppleReleaseReceipt InvokeShip(bool plan)
    {
        AssertOwner();
        if (plan) RetainPreviousSourcePlan();
        var startedAt = DateTimeOffset.UtcNow;
        var arguments = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_policy.OwnerRoot, "scripts/Invoke-PinnedPowerForge.ps1"), "-ExpectedCommit", _policy.OwnerCommit,
            "-ConsumerRoot", _policy.ConsumerRoot, "-ExpectedConsumerRepository", _policy.Repository,
            "apple-release", "Ship", "--config", _policy.ConfigPath, "--apple-testflight-target", string.Join(",", _policy.Targets.Select(value => value.Name)), "--summary", "--output", "json" };
        if (plan)
        {
            arguments.Add("--plan");
            if (_running?.Stage == AppleTestFlightJobStage.AppleProcessingPending && _running.PlanSha256 is not null)
                arguments.AddRange(new[] { "--apple-expected-plan-sha256", _running.PlanSha256 });
        }
        else arguments.AddRange(new[] { "--apple-expected-plan-sha256", _running!.PlanSha256!, "--confirm-apple-action" });
        Process("pwsh", _policy.ConsumerRoot, arguments, apple: true);
        var spec = ReadSpec();
        var automation = spec.AppleApps!.Automation;
        var path = ContainedPath(_policy.ConsumerRoot, plan ? automation.PlanReceiptPath : automation.ReceiptPath);
        if (plan && !File.Exists(path)) throw new AppleTestFlightToolException("Ship preview produced no fresh plan; retry this nonmutating stage.");
        var receipt = JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(File.ReadAllText(path), Json) ?? throw new InvalidOperationException("Ship receipt is missing.");
        if (plan && receipt.CheckedAt < startedAt) throw new AppleTestFlightToolException("Ship preview produced no fresh plan; retry this nonmutating stage.");
        if (receipt.SourceCommit != _running!.Source || receipt.Action != PowerForgeAppleReleaseAction.Ship || receipt.PlanOnly != plan || receipt.CheckedAt < startedAt)
            throw new InvalidOperationException("Ship receipt is stale or belongs to another source or action.");
        return receipt;
    }
}

/// <summary>A tool boundary failure that can be retried only outside mutation stages.</summary>
internal sealed class AppleTestFlightToolException(string message) : Exception(message);
internal sealed class AppleTestFlightPreMutationRejectedException() : Exception("The child definitively rejected entry before mutation; retain the prior stage and retry when permitted.");
#endif
