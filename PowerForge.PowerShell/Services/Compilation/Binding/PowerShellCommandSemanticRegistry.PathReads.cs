namespace PowerForge;

internal sealed partial class PowerShellCommandSemanticRegistry
{
    private static IEnumerable<PowerShellCompilationCommandProviderContract> CreatePathReads()
    {
        foreach (var (id, name, aliases, switches, values) in PathReadShapes())
        {
            var contract = Contract("powerforge.command.path-read." + id,
                PowerShellCompilationCommandFamily.HostedRegion, name, aliases,
                PowerShellCompilationCommandOutput.Unknown, PowerShellCompilationCommandCardinality.Unknown,
                "Success+PowerShell", PowerShellCompilationCommandErrors.PowerShellHost, runtimeFree: false,
                moduleNames: new[] { "Microsoft.PowerShell.Management" });
            contract.Parameters = new[]
                {
                    new PowerShellCompilationCommandParameterContract { Name = "Path", Position = 0 },
                    new PowerShellCompilationCommandParameterContract { Name = "LiteralPath", Aliases = new[] { "PSPath" }, Position = -1 }
                }
                .Concat(new[] { "Force", "Verbose", "Debug" }.Concat(switches).Select(parameter =>
                    new PowerShellCompilationCommandParameterContract { Name = parameter, Position = -1 }))
                .Concat(new[] { "Filter", "Include", "Exclude", "Credential", "ErrorAction", "WarningAction", "InformationAction", "ProgressAction", "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable" }
                    .Concat(values).Select(parameter => new PowerShellCompilationCommandParameterContract { Name = parameter, Position = -1 }))
                .ToArray();
            yield return contract;
        }
    }

    // The public provider parameter contract has no switch-arity field. Keep this
    // bounded read-family fact in the existing catalog rather than adding a public API.
    internal static bool IsPathReadSwitch(string providerId, string parameterName)
        => new[] { "Force", "Verbose", "Debug" }.Contains(parameterName, StringComparer.OrdinalIgnoreCase) ||
           PathReadShapes().Any(shape => providerId.Equals("powerforge.command.path-read." + shape.Id, StringComparison.Ordinal) &&
               shape.Switches.Contains(parameterName, StringComparer.OrdinalIgnoreCase));

    // Only this existing provider contract owns a safe default-to-current-location read.
    // Admission still requires a standalone command without authored operands.
    internal static bool AllowsImplicitPathReadCurrentLocation(string providerId)
        => providerId.Equals("powerforge.command.path-read.children", StringComparison.Ordinal);

    private static IEnumerable<(string Id, string Name, string[] Aliases, string[] Switches, string[] Values)> PathReadShapes()
        => new[]
        {
            ("content", "Get-Content", new[] { "gc", "cat", "type" }, new[] { "Raw", "Wait", "AsByteStream" }, new[] { "ReadCount", "TotalCount", "Tail", "Delimiter", "Encoding", "Stream" }),
            ("item", "Get-Item", new[] { "gi" }, Array.Empty<string>(), new[] { "Stream" }),
            ("children", "Get-ChildItem", new[] { "gci", "dir", "ls" }, new[] { "Recurse", "Name", "File", "Directory", "Hidden", "ReadOnly", "System", "FollowSymlink" }, new[] { "Depth", "Attributes" })
        };
}
