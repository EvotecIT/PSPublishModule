using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private void SignMsiPackage(
        DotNetPublishPlan plan,
        IList<DotNetPublishMsiBuildResult> msiBuilds,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (msiBuilds is null) throw new ArgumentNullException(nameof(msiBuilds));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var installerId = (step.InstallerId ?? string.Empty).Trim();
        var target = (step.TargetName ?? string.Empty).Trim();
        var framework = (step.Framework ?? string.Empty).Trim();
        var runtime = (step.Runtime ?? string.Empty).Trim();
        var style = step.Style;

        if (string.IsNullOrWhiteSpace(installerId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing InstallerId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");

        var build = msiBuilds
            .LastOrDefault(b =>
                string.Equals(b.InstallerId, installerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Target, target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Framework, framework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Runtime, runtime, StringComparison.OrdinalIgnoreCase)
                && b.Style == style.Value);
        if (build is null)
        {
            throw new InvalidOperationException(
                $"MSI sign step '{step.Key}' could not find matching msi.build result for " +
                $"installer='{installerId}', target='{target}', framework='{framework}', runtime='{runtime}', style='{style.Value}'.");
        }

        var installer = (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
            .FirstOrDefault(i => string.Equals(i.Id, installerId, StringComparison.OrdinalIgnoreCase));
        if (installer?.Sign is null || !installer.Sign.Enabled)
        {
            throw new InvalidOperationException(
                $"MSI sign step '{step.Key}' requires installer signing configuration for installer '{installerId}'.");
        }

        var outputs = (build.OutputFiles ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (outputs.Length == 0)
        {
            _logger.Warn(
                $"MSI sign for '{installerId}' skipped because msi.build produced no detectable output files " +
                $"for {target} ({framework}, {runtime}, {style.Value}).");
            build.SignedFiles = Array.Empty<string>();
            return;
        }

        var signed = TrySignFiles(
            files: outputs,
            workingDirectory: plan.ProjectRoot,
            sign: installer.Sign,
            scope: $"msi:{installerId}");

        build.SignedFiles = signed;
        _logger.Info(
            $"MSI sign completed for '{installerId}' ({target}, {framework}, {runtime}, {style.Value}) -> {signed.Length}/{outputs.Length} signed.");
    }
}
