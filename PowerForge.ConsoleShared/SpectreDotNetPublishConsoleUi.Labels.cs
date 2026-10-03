using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static partial class SpectreDotNetPublishConsoleUi
{
    private static string GetStepTitle(DotNetPublishStep step, DotNetPublishPlan plan)
    {
        if (step.Kind == DotNetPublishStepKind.Publish)
        {
            var target = step.TargetName ?? string.Empty;
            var rid = step.Runtime ?? string.Empty;

            var tp = plan.Targets.FirstOrDefault(t => t.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            var framework = step.Framework ?? tp?.Publish.Framework;
            var style = (step.Style ?? tp?.Publish.Style)?.ToString();

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(rid)) details.Add(rid);
            if (!string.IsNullOrWhiteSpace(framework)) details.Add(framework!);
            if (!string.IsNullOrWhiteSpace(style)) details.Add(style!);

            var suffix = details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty;
            return $"Publish {target}{suffix}".Trim();
        }

        if (step.Kind == DotNetPublishStepKind.MsiPrepare)
        {
            var installer = string.IsNullOrWhiteSpace(step.InstallerId) ? "(installer)" : step.InstallerId;
            var target = string.IsNullOrWhiteSpace(step.TargetName) ? string.Empty : $" from {step.TargetName}";
            var suffixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(step.Runtime)) suffixParts.Add(step.Runtime!);
            if (!string.IsNullOrWhiteSpace(step.Framework)) suffixParts.Add(step.Framework!);
            if (step.Style.HasValue) suffixParts.Add(step.Style.Value.ToString());
            var suffix = suffixParts.Count == 0 ? string.Empty : $" ({string.Join(", ", suffixParts)})";
            return $"MSI prepare {installer}{target}{suffix}".Trim();
        }

        if (step.Kind == DotNetPublishStepKind.MsiBuild)
        {
            var installer = string.IsNullOrWhiteSpace(step.InstallerId) ? "(installer)" : step.InstallerId;
            var target = string.IsNullOrWhiteSpace(step.TargetName) ? string.Empty : $" from {step.TargetName}";
            var suffixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(step.Runtime)) suffixParts.Add(step.Runtime!);
            if (!string.IsNullOrWhiteSpace(step.Framework)) suffixParts.Add(step.Framework!);
            if (step.Style.HasValue) suffixParts.Add(step.Style.Value.ToString());
            var suffix = suffixParts.Count == 0 ? string.Empty : $" ({string.Join(", ", suffixParts)})";
            return $"MSI build {installer}{target}{suffix}".Trim();
        }

        if (step.Kind == DotNetPublishStepKind.MsiSign)
        {
            var installer = string.IsNullOrWhiteSpace(step.InstallerId) ? "(installer)" : step.InstallerId;
            var target = string.IsNullOrWhiteSpace(step.TargetName) ? string.Empty : $" from {step.TargetName}";
            var suffixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(step.Runtime)) suffixParts.Add(step.Runtime!);
            if (!string.IsNullOrWhiteSpace(step.Framework)) suffixParts.Add(step.Framework!);
            if (step.Style.HasValue) suffixParts.Add(step.Style.Value.ToString());
            var suffix = suffixParts.Count == 0 ? string.Empty : $" ({string.Join(", ", suffixParts)})";
            return $"MSI sign {installer}{target}{suffix}".Trim();
        }

        if (step.Kind == DotNetPublishStepKind.BenchmarkExtract)
        {
            var gate = string.IsNullOrWhiteSpace(step.GateId) ? "(gate)" : step.GateId;
            return $"Benchmark extract {gate}".Trim();
        }

        if (step.Kind == DotNetPublishStepKind.BenchmarkGate)
        {
            var gate = string.IsNullOrWhiteSpace(step.GateId) ? "(gate)" : step.GateId;
            return $"Benchmark gate {gate}".Trim();
        }

        var title = string.IsNullOrWhiteSpace(step.Title) ? step.Kind.ToString() : step.Title.Trim();
        if (step.Kind != DotNetPublishStepKind.Publish && !string.IsNullOrWhiteSpace(step.Runtime))
            title = $"{title} ({step.Runtime})";
        return $"{title}".Trim();
    }

}
