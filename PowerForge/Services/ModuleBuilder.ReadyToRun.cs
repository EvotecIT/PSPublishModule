using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class ModuleBuilder
{
    private static readonly Regex RuntimeIdentifierPattern = new(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks the ReadyToRun options against the module's payloads and returns the normalized options, or null when
    /// ReadyToRun is off. The precompiled payloads only add to a portable PowerShell 7 payload, never replace it.
    /// </summary>
    internal static ModuleReadyToRunOptions? ValidateReadyToRun(ModuleReadyToRunOptions? options, IReadOnlyList<ModuleBinaryPayload> payloads)
    {
        if (options is null || !options.Enabled)
            return null;

        var framework = string.IsNullOrWhiteSpace(options.Framework) ? ModuleReadyToRunOptions.DefaultFramework : options.Framework.Trim();
        var frameworkPayload = ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { framework })[0];
        // A platform-qualified framework such as net10.0-windows is fine: the runtime identifier already picks the platform.
        if (frameworkPayload.Kind != ModuleBinaryPayloadKind.Core || frameworkPayload.Version.Major < 5)
            throw new InvalidOperationException(
                $"ReadyToRun framework '{framework}' is not supported. Use .NET 5 or later, such as net10.0.");

        if (!payloads.Any(static payload => payload.Kind is ModuleBinaryPayloadKind.Core or ModuleBinaryPayloadKind.Standard))
            throw new InvalidOperationException(
                "ReadyToRun payloads need a portable PowerShell 7 payload to fall back to on other platforms and runtimes. " +
                "Add a framework such as net8.0 to the module frameworks.");

        var runtimeIdentifiers = (options.RuntimeIdentifiers is { Length: > 0 }
                ? options.RuntimeIdentifiers
                : ModuleReadyToRunOptions.DefaultRuntimeIdentifiers)
            .Where(static runtimeIdentifier => !string.IsNullOrWhiteSpace(runtimeIdentifier))
            .Select(static runtimeIdentifier => runtimeIdentifier.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var invalid = runtimeIdentifiers.Where(static runtimeIdentifier =>
                !RuntimeIdentifierPattern.IsMatch(runtimeIdentifier) || runtimeIdentifier.IndexOf('-') < 0 ||
                runtimeIdentifier.Equals("any", StringComparison.Ordinal))
            .ToArray();
        if (invalid.Length > 0)
            throw new InvalidOperationException(
                $"ReadyToRun runtime identifiers must be platform-specific, such as win-x64 or osx-arm64: {string.Join(", ", invalid)}.");
        if (runtimeIdentifiers.Length == 0)
            throw new InvalidOperationException("ReadyToRun needs at least one runtime identifier.");

        return new ModuleReadyToRunOptions
        {
            Enabled = true,
            Framework = framework,
            RuntimeIdentifiers = runtimeIdentifiers,
            Scope = options.Scope
        };
    }

    private void PublishReadyToRunPayloads(
        Options opts,
        ModuleReadyToRunOptions readyToRun,
        DotnetPublisher publisher,
        string artifactsRoot,
        string libRoot,
        ISet<string> exportAssemblyFileNames)
    {
        _logger.Info(
            $"Publishing ReadyToRun payloads ({readyToRun.Framework}, {readyToRun.Scope}) for {string.Join(", ", readyToRun.RuntimeIdentifiers)}.");
        var publishes = publisher.PublishReadyToRun(
            opts.CsprojPath!,
            opts.Configuration,
            readyToRun.Framework,
            readyToRun.RuntimeIdentifiers,
            readyToRun.Scope,
            opts.ModuleVersion,
            artifactsRoot,
            opts.NuGetRestoreSources);

        foreach (var runtimeIdentifier in readyToRun.RuntimeIdentifiers)
        {
            var target = Path.Combine(libRoot, ModuleBinaryPayloadLayout.GetReadyToRunFolderName(runtimeIdentifier));
            Directory.CreateDirectory(target);
            CopyPublishOutputBinaries(
                publishes[runtimeIdentifier],
                target,
                readyToRun.Framework,
                exportAssemblyFileNames,
                new PublishCopyOptions(opts.ExcludeLibraryFilter, opts.DoNotCopyLibrariesRecursively, opts.HandleRuntimes)
                {
                    PreserveSystemManagement = opts.UseAssemblyLoadContext
                });
            File.WriteAllText(Path.Combine(target, ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName), readyToRun.Framework);
        }
    }
}
