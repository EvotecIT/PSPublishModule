namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private async Task ValidateToolAsync(DotNetToolValidation spec, Dictionary<string, string> variables,
        ReleaseValidationReport report, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.PackageId) || string.IsNullOrWhiteSpace(spec.CommandName) ||
            Path.GetFileName(spec.CommandName) != spec.CommandName || spec.CommandName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            throw new InvalidOperationException("Tool validation requires a package identity and a simple command name.");
        var matches = InspectPrimaryPackages(Resolve(spec.PackageRoot, variables))
            .Where(p => string.Equals(p.Id, spec.PackageId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Expected exactly one staged tool package '{spec.PackageId}'.");
        var package = matches[0];
        if (!string.IsNullOrEmpty(report.Version) && !string.Equals(report.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tool package version {package.Version} does not match {report.Version}.");
        if (string.IsNullOrEmpty(report.Version)) report.Version = package.Version;
        variables["Version"] = report.Version;
        foreach (var manifestInstall in spec.IncludeManifestInstall ? new[] { false, true } : new[] { false })
        {
            using var workspace = new ValidationWorkspace();
            var feed = Directory.CreateDirectory(Path.Combine(workspace.Root, "feed")).FullName;
            File.Copy(package.Path, Path.Combine(feed, Path.GetFileName(package.Path)));
            var config = WriteValidationNuGetConfig(workspace.Root, feed, new[] { package.Id }, Array.Empty<string>());
            var toolRoot = Path.Combine(workspace.Root, "tools");
            var environment = new Dictionary<string, string?>
            {
                ["NUGET_PACKAGES"] = Path.Combine(workspace.Root, "packages"),
                ["DOTNET_CLI_HOME"] = workspace.Root
            };
            var values = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase)
            {
                ["Version"] = package.Version, ["WorkRoot"] = workspace.Root,
                ["ToolPath"] = Path.Combine(toolRoot, spec.CommandName + (FrameworkCompatibility.IsWindows() ? ".exe" : string.Empty))
            };
            if (manifestInstall)
                await RunCommandAsync(new ReleaseCommandValidation
                {
                    Name = "Create local tool manifest", FileName = "dotnet", WorkingDirectory = workspace.Root,
                    Arguments = new[] { "new", "tool-manifest" }, Environment = environment
                }, values, report, cancellationToken).ConfigureAwait(false);
            var install = new List<string> { "tool", "install", package.Id, "--version", package.Version, "--configfile", config, "--no-cache" };
            if (manifestInstall) install.Add("--local");
            else install.AddRange(new[] { "--tool-path", toolRoot });
            await RunCommandAsync(new ReleaseCommandValidation
            {
                Name = "Install " + package.Id + (manifestInstall ? " (manifest)" : " (tool path)"), FileName = "dotnet",
                WorkingDirectory = workspace.Root, Arguments = install.ToArray(), Environment = environment, TimeoutSeconds = 600
            }, values, report, cancellationToken).ConfigureAwait(false);
            foreach (var command in spec.Commands)
            {
                var childEnvironment = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in command.Environment) childEnvironment[pair.Key] = pair.Value;
                var localTool = manifestInstall && string.Equals(command.FileName, "{ToolPath}", StringComparison.OrdinalIgnoreCase);
                var workingDirectory = command.WorkingDirectory ?? workspace.Root;
                if (localTool && IsCommandApplicable(command))
                {
                    // dotnet tool run discovers its manifest from the working directory; it has no manifest-path option.
                    var resolvedDirectory = Resolve(workingDirectory, values).TrimEnd(Path.DirectorySeparatorChar);
                    if (!string.Equals(resolvedDirectory, workspace.Root.TrimEnd(Path.DirectorySeparatorChar),
                            FrameworkCompatibility.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException($"{command.Name}: manifest tool probes must use the default working directory or {{WorkRoot}}. Disable IncludeManifestInstall for a tool-path-only probe with another working directory.");
                }
                await RunCommandAsync(new ReleaseCommandValidation
                {
                    Name = command.Name + (manifestInstall ? " (manifest)" : " (tool path)"),
                    FileName = localTool ? "dotnet" : command.FileName,
                    Arguments = localTool ? new[] { "tool", "run", spec.CommandName, "--" }.Concat(command.Arguments).ToArray() : command.Arguments,
                    WorkingDirectory = workingDirectory, Environment = childEnvironment,
                    TimeoutSeconds = command.TimeoutSeconds, ExpectedExitCode = command.ExpectedExitCode,
                    ExpectedOutput = command.ExpectedOutput, OutputContains = command.OutputContains,
                    OutputJsonKind = command.OutputJsonKind, MinimumJsonItems = command.MinimumJsonItems,
                    NonEmptyFiles = command.NonEmptyFiles, Platforms = command.Platforms
                }, values, report, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
