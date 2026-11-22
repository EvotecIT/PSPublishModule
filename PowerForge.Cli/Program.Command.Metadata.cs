using System.Reflection;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleManifestUsage =
        "Usage: powerforge module manifest --path <module.psd1> [--output json]";

    private static int CommandVersion(string[] args, Assembly versionAssembly)
    {
        var version = versionAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? versionAssembly.GetName().Version?.ToString() ?? "unknown";

        if (IsJsonOutput(args))
            WriteModuleCommandResult("version", new CliVersionResult(version), ModuleCliJson.Default.CliVersionResult, logBuffer: null);
        else
            Console.WriteLine(version);

        return 0;
    }

    private static int InspectModuleManifest(string[] args, bool outputJson, BufferedLogger? logBuffer)
    {
        var (options, _) = ParseModuleCommandOptions(args, "manifest");
        var path = RequireModuleOption(options, "--path", ModuleManifestUsage);
        var metadata = new ModuleManifestMetadataReader().Inspect(path);
        if (outputJson)
        {
            WriteModuleCommandResult("module.manifest", metadata, ModuleCliJson.Default.ModuleManifestMetadata, logBuffer);
        }
        else
        {
            var version = metadata.ModuleVersion;
            if (metadata.PreRelease is { } prerelease)
                version = $"{version}-{prerelease}";
            Console.WriteLine($"{metadata.ModuleName} {version}");
        }

        return 0;
    }
}
