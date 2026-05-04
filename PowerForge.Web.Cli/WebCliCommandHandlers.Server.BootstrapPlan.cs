using System.Text;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerBootstrapPlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.bootstrap-plan");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var outPathArg = TryGetOptionValue(subArgs, "--out") ??
                         TryGetOptionValue(subArgs, "--output") ??
                         TryGetOptionValue(subArgs, "--output-dir");
        var outputRoot = ResolveBootstrapPlanOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);

        var warnings = new List<string>();
        var steps = BuildBootstrapPlanSteps(manifest, warnings);
        var markdownPath = Path.Combine(outputRoot, "bootstrap-plan.md");
        var scriptPath = Path.Combine(outputRoot, "bootstrap-plan.sh");
        WriteBootstrapPlanMarkdown(markdownPath, manifest, steps, warnings);
        WriteBootstrapPlanScript(scriptPath, steps);

        var result = new PowerForgeServerBootstrapPlanResult
        {
            ManifestPath = manifestPath,
            OutputPath = outputRoot,
            MarkdownPath = markdownPath,
            ScriptPath = scriptPath,
            Steps = steps.ToArray(),
            Warnings = warnings.ToArray()
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "bootstrap-plan.json"),
            JsonSerializer.Serialize(result, WebCliJson.Context.PowerForgeServerBootstrapPlanResult));

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.bootstrap-plan",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerBootstrapPlanResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return 0;
        }

        logger.Success("Server bootstrap plan generated.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Markdown: {markdownPath}");
        logger.Info($"Script draft: {scriptPath}");
        logger.Info($"Steps: {steps.Count}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return 0;
    }

    private static string ResolveBootstrapPlanOutputPath(string? outPathArg, PowerForgeServerRecoveryManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(outPathArg))
            return Path.GetFullPath(outPathArg);

        var name = SanitizeFileName(string.IsNullOrWhiteSpace(manifest.Name) ? "server" : manifest.Name);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_server-state", name, "bootstrap-plan"));
    }

    private static List<PowerForgeServerBootstrapPlanStep> BuildBootstrapPlanSteps(
        PowerForgeServerRecoveryManifest manifest,
        ICollection<string> warnings)
    {
        var steps = new List<PowerForgeServerBootstrapPlanStep>();
        var plannedCommands = new HashSet<string>(StringComparer.Ordinal);
        var order = 1;

        AddStep(steps, ref order, "preflight", "Confirm supported host",
            $"test -f /etc/os-release && grep -q {ShellQuote((manifest.Target?.Os ?? "ubuntu").Replace("ubuntu-", string.Empty, StringComparison.OrdinalIgnoreCase))} /etc/os-release || true",
            plannedCommands: plannedCommands);

        if (manifest.Packages?.Apt?.Length > 0)
        {
            AddStep(steps, ref order, "packages", "Install apt prerequisites",
                "apt-get update && apt-get install -y " + string.Join(' ', manifest.Packages.Apt),
                plannedCommands: plannedCommands);
        }

        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Path)) continue;
            var owner = string.IsNullOrWhiteSpace(path.Owner) ? "root" : path.Owner;
            var group = string.IsNullOrWhiteSpace(path.Group) ? "root" : path.Group;
            var mode = string.IsNullOrWhiteSpace(path.Mode) ? "755" : path.Mode;
            if (path.Kind?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true)
            {
                AddStep(steps, ref order, "filesystem", $"Create directory {path.Path}",
                    $"install -d -o {owner} -g {group} -m {mode} {ShellQuote(path.Path)}",
                    plannedCommands: plannedCommands);
            }
        }

        foreach (var repository in manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
        {
            if (string.IsNullOrWhiteSpace(repository.Path)) continue;
            if (string.IsNullOrWhiteSpace(repository.Url))
            {
                warnings.Add($"Repository URL is missing for role '{repository.Role}'. Bootstrap script leaves a manual clone step.");
                AddStep(steps, ref order, "repositories", $"Clone {repository.Role} repository", $"# TODO: git clone <{repository.Role}-repo-url> {ShellQuote(repository.Path)}", manual: true, plannedCommands: plannedCommands);
            }
            else
            {
                var branchArg = string.IsNullOrWhiteSpace(repository.Branch) ? string.Empty : $" --branch {ShellQuote(repository.Branch)}";
                var gitDirectory = repository.Path.TrimEnd('/') + "/.git";
                AddStep(steps, ref order, "repositories", $"Clone or update {repository.Role} repository",
                    $"if [ -d {ShellQuote(gitDirectory)} ]; then git -C {ShellQuote(repository.Path)} fetch --all --prune; else git clone{branchArg} {ShellQuote(repository.Url)} {ShellQuote(repository.Path)}; fi",
                    plannedCommands: plannedCommands);
            }
        }

        foreach (var command in manifest.Bootstrap?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command)) continue;
            AddStep(steps, ref order, "bootstrap", command.Id ?? "bootstrap command", command.Command, command.Sensitive, plannedCommands: plannedCommands);
        }

        var apacheModules = manifest.Packages?.ApacheModules ?? manifest.Apache?.Modules ?? Array.Empty<string>();
        if (apacheModules.Length > 0)
            AddStep(steps, ref order, "apache", "Enable Apache modules", "a2enmod " + string.Join(' ', apacheModules), plannedCommands: plannedCommands);

        foreach (var file in (manifest.Apache?.Sites ?? Array.Empty<PowerForgeServerManagedFile>())
                 .Concat(manifest.Apache?.Conf ?? Array.Empty<PowerForgeServerManagedFile>()))
        {
            if (string.IsNullOrWhiteSpace(file.Source) || string.IsNullOrWhiteSpace(file.Target)) continue;
            AddStep(steps, ref order, "apache", $"Install Apache file {file.Target}",
                $"install -m 0644 {ShellQuote(file.Source)} {ShellQuote(file.Target)}",
                plannedCommands: plannedCommands);
        }

        foreach (var unit in (manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
                 .Concat(manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>()))
        {
            if (!string.IsNullOrWhiteSpace(unit.Source) && !string.IsNullOrWhiteSpace(unit.Target))
            {
                AddStep(steps, ref order, "systemd", $"Install systemd unit {unit.Name}",
                    $"install -m 0644 {ShellQuote(unit.Source)} {ShellQuote(unit.Target)}",
                    plannedCommands: plannedCommands);
            }
        }

        AddStep(steps, ref order, "systemd", "Reload systemd units", "systemctl daemon-reload", plannedCommands: plannedCommands);

        foreach (var unit in (manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
                 .Concat(manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
                 .Where(static unit => unit.Enabled && !string.IsNullOrWhiteSpace(unit.Name)))
        {
            AddStep(steps, ref order, "systemd", $"Enable {unit.Name}", $"systemctl enable {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
        }

        foreach (var port in manifest.Firewall?.SshPorts ?? Array.Empty<int>())
            AddStep(steps, ref order, "firewall", $"Allow SSH port {port}", $"ufw allow {port}/tcp", plannedCommands: plannedCommands);

        if (!string.IsNullOrWhiteSpace(manifest.Firewall?.SyncCommand))
            AddStep(steps, ref order, "firewall", "Apply origin firewall sync", manifest.Firewall.SyncCommand, plannedCommands: plannedCommands);

        AddStep(steps, ref order, "firewall", "Enable UFW", "ufw --force enable", plannedCommands: plannedCommands);

        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
        {
            AddStep(steps, ref order, "secrets", $"Restore secret {secret.Id}",
                $"# TODO: restore encrypted secret to {secret.Path ?? secret.Env ?? secret.Id}", manual: true, sensitive: true, plannedCommands: plannedCommands);
        }

        foreach (var command in manifest.Deploy?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command)) continue;
            var shell = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? command.Command
                : $"cd {ShellQuote(command.WorkingDirectory)} && {command.Command}";
            AddStep(steps, ref order, "deploy", command.Id ?? "deploy command", shell, command.Sensitive, plannedCommands: plannedCommands);
        }

        AddStep(steps, ref order, "verify", "Run PowerForge server verify", "# Run from an operator workstation: powerforge-web server verify --manifest <manifest> --fail-on-failure", manual: true, plannedCommands: plannedCommands);
        return steps;
    }

    private static void AddStep(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        string category,
        string title,
        string? command,
        bool sensitive = false,
        bool manual = false,
        ISet<string>? plannedCommands = null)
    {
        if (!string.IsNullOrWhiteSpace(command) &&
            plannedCommands is not null &&
            !command.TrimStart().StartsWith("#", StringComparison.Ordinal) &&
            !plannedCommands.Add(command))
        {
            return;
        }

        steps.Add(new PowerForgeServerBootstrapPlanStep
        {
            Order = order++,
            Category = category,
            Title = title,
            Command = command,
            Sensitive = sensitive,
            Manual = manual
        });
    }

    private static void WriteBootstrapPlanMarkdown(
        string path,
        PowerForgeServerRecoveryManifest manifest,
        IReadOnlyList<PowerForgeServerBootstrapPlanStep> steps,
        IReadOnlyList<string> warnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PowerForge Server Bootstrap Plan");
        builder.AppendLine();
        builder.AppendLine($"Manifest: `{manifest.Name}`");
        builder.AppendLine($"Target: `{manifest.Target?.SshAlias ?? manifest.Target?.Host ?? "new host"}`");
        builder.AppendLine();
        builder.AppendLine("This is a review artifact. It is safe to commit the plan, but not generated secret bundles.");
        builder.AppendLine();

        foreach (var group in steps.GroupBy(static step => step.Category))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var step in group)
            {
                builder.AppendLine($"{step.Order}. {step.Title}");
                if (step.Manual) builder.AppendLine("   - Manual review/action required.");
                if (step.Sensitive) builder.AppendLine("   - Sensitive step; do not paste secret values into Git.");
                if (!string.IsNullOrWhiteSpace(step.Command))
                {
                    builder.AppendLine();
                    builder.AppendLine("```bash");
                    builder.AppendLine(step.Command);
                    builder.AppendLine("```");
                }
                builder.AppendLine();
            }
        }

        if (warnings.Count > 0)
        {
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in warnings)
                builder.AppendLine($"- {warning}");
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteBootstrapPlanScript(
        string path,
        IReadOnlyList<PowerForgeServerBootstrapPlanStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -Eeuo pipefail");
        builder.AppendLine();
        builder.AppendLine("# Generated by powerforge-web server bootstrap-plan.");
        builder.AppendLine("# Review before running. Manual/TODO steps intentionally remain comments.");
        builder.AppendLine();

        foreach (var step in steps)
        {
            builder.AppendLine($"# {step.Order}. [{step.Category}] {step.Title}");
            if (string.IsNullOrWhiteSpace(step.Command))
            {
                builder.AppendLine();
                continue;
            }

            if (step.Manual || step.Sensitive || step.Command.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                foreach (var line in step.Command.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
                    builder.AppendLine("# " + line);
                if (step.Manual)
                {
                    builder.AppendLine($"echo {ShellQuote($"Manual bootstrap step required: {step.Title}")} >&2");
                    builder.AppendLine("exit 3");
                }
            }
            else
            {
                builder.AppendLine(step.Command);
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
