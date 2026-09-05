using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static void EnsureStrictDependencyClosureCertified(PowerShellCompilationDependencyClosure? closure)
    {
        if (closure?.Verified == true && closure.Limitations.Count == 0)
            return;

        var limitations = closure?.Limitations
            .Where(static limitation => !string.IsNullOrWhiteSpace(limitation))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        var detail = limitations.Length == 0
            ? "The delivered dependency closure did not produce positive certification evidence."
            : string.Join(" ", limitations);
        throw new InvalidOperationException(
            "Strict runtime-free artifact publication requires a fully certified delivered dependency closure. " + detail);
    }

    internal static bool ShouldEnablePublishSingleFile(PowerShellCompilationBuildSpec spec)
        => spec.SingleFile && spec.Optimization != PowerShellCompilationExecutableOptimization.NativeAot;

    private static GeneratedBuildProcessResult RunDotNetBuild(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string publishDirectory,
        string? runtimeIdentifier,
        bool restoreCompleted)
    {
        var arguments = new List<string>
        {
            spec.Kind == PowerShellCompilationArtifactKind.Executable ? "publish" : "build",
            projectPath,
            "--configuration", "Release",
            "--output", publishDirectory,
            "--nologo",
            "--verbosity", "minimal"
        };
        if (restoreCompleted) arguments.Add("--no-restore");
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(runtimeIdentifier!);
        }

        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet",
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                TimeSpan.FromSeconds(spec.TimeoutSeconds),
                GetNuGetEnvironment(spec)))
            .GetAwaiter()
            .GetResult();
        var output = string.IsNullOrWhiteSpace(run.StdErr)
            ? run.StdOut
            : run.StdOut + Environment.NewLine + run.StdErr;
        return new GeneratedBuildProcessResult(run.ExitCode, output, run.TimedOut);
    }

    private static GeneratedBuildProcessResult RunDotNetRestore(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string? runtimeIdentifier)
    {
        var arguments = new List<string>
        {
            "restore", projectPath, "--nologo", "--verbosity", "minimal"
        };
        if (spec.OfflineRestore)
        {
            arguments.Add("--ignore-failed-sources");
            arguments.Add("--no-cache");
        }
        if (!string.IsNullOrWhiteSpace(spec.NuGetLockFilePath))
        {
            arguments.Add("--locked-mode");
            arguments.Add("--property:NuGetLockFilePath=" + Path.Combine(
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                "packages.lock.json"));
        }
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(runtimeIdentifier!);
        }
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
            arguments,
            TimeSpan.FromSeconds(spec.TimeoutSeconds),
            GetNuGetEnvironment(spec))).GetAwaiter().GetResult();
        var output = string.IsNullOrWhiteSpace(run.StdErr) ? run.StdOut : run.StdOut + Environment.NewLine + run.StdErr;
        return new GeneratedBuildProcessResult(run.ExitCode, output, run.TimedOut);
    }

    private static IReadOnlyDictionary<string, string?>? GetNuGetEnvironment(PowerShellCompilationBuildSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.NuGetPackageRoot))
            return null;
        var root = Path.GetFullPath(spec.NuGetPackageRoot!.Trim().Trim('"'));
        Directory.CreateDirectory(root);
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["NUGET_PACKAGES"] = root
        };
    }

    private static string GetPowerShellSdkVersion(string targetFramework)
        => targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "7.6.5" : "7.4.18";

    private static string GetSecurityXmlVersion(string targetFramework)
        => "10.0.11";

    private static string GetPowerShellReference(string targetFramework)
        => targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
            ? "<PackageReference Include=\"Microsoft.PowerShell.5.ReferenceAssemblies\" Version=\"1.1.0\" PrivateAssets=\"all\" />"
            : $"<PackageReference Include=\"Microsoft.PowerShell.SDK\" Version=\"{GetPowerShellSdkVersion(targetFramework)}\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />{Environment.NewLine}    " +
              $"<PackageReference Include=\"System.Security.Cryptography.Xml\" Version=\"{GetSecurityXmlVersion(targetFramework)}\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />";

    private static string ReadTemplate(string resourceName)
    {
        using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded compilation template '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteManifest(string path, PowerShellCompilationArtifactManifest manifest)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string SanitizeArtifactName(string value)
    {
        var sanitized = new string(value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            throw new ArgumentException("Artifact name does not contain a usable file name.", nameof(value));
        if (new[] { ".exe", ".dll", ".pdb", ".generated", ".powerforge-compilation.json" }
            .Any(suffix => sanitized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Artifact name must not end with a generated artifact suffix because it can overlap another artifact set.", nameof(value));
        PowerShellArtifactSetPublisher.EnsureArtifactNameIsNotReserved(sanitized, nameof(value));
        return sanitized;
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string GetBinaryModuleAssemblyVersion(PowerShellCompilationBuildSpec spec)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.BinaryModule)
            return "1.0.0.0";
        var manifestPath = PowerShellCompiledModuleManifest.ResolveSourceManifest(spec.SourcePath, spec.ModuleManifestPath);
        if (!File.Exists(manifestPath)) return "1.0.0.0";
        var value = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(value)) return "1.0.0.0";
        if (!Version.TryParse(value, out var version))
            throw new InvalidOperationException($"Module manifest '{manifestPath}' declares invalid ModuleVersion '{value}'.");
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision)).ToString(4);
    }

    private static string BoundOutput(string output)
        => output.Length <= MaximumBuildOutputLength ? output : output.Substring(output.Length - MaximumBuildOutputLength);

    private static string DescribeBlockers(IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
        => string.Join(" ", diagnostics.Select(static diagnostic => diagnostic.Message)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal));

    private sealed class CopiedArtifact
    {
        internal CopiedArtifact(string primaryPath, PowerShellCompilationArtifactFile[] files)
        {
            PrimaryPath = primaryPath;
            Files = files;
        }

        internal string PrimaryPath { get; }
        internal PowerShellCompilationArtifactFile[] Files { get; }

        internal CopiedArtifact WithAdditionalFiles(IEnumerable<PowerShellCompilationArtifactFile> files)
            => new(PrimaryPath, Files.Concat(files).ToArray());

        internal CopiedArtifact WithReplacementFiles(IEnumerable<PowerShellCompilationArtifactFile> files)
        {
            var replacements = files.ToArray();
            if (replacements.Length == 0) return this;
            var retained = Files.Where(existing => !replacements.Any(replacement =>
                PowerShellCompilationPathSafety.PathEquals(existing.Path, replacement.Path)));
            return new CopiedArtifact(PrimaryPath, retained.Concat(replacements).ToArray());
        }
    }

    private sealed class GeneratedBuildProcessResult
    {
        internal GeneratedBuildProcessResult(int exitCode, string output, bool timedOut)
        {
            ExitCode = exitCode;
            Output = output;
            TimedOut = timedOut;
        }

        internal int ExitCode { get; }
        internal string Output { get; }
        internal bool TimedOut { get; }
    }

    private sealed class PackagedSourceSet
    {
        internal PackagedSourceSet(
            string entryRelativePath,
            string projectResources,
            string dependencySpecs,
            bool hasDependencies,
            string[] embeddedScriptPaths,
            string[] embeddedResourceRelativePaths,
            bool usesExtractedRoot)
        {
            EntryRelativePath = entryRelativePath;
            ProjectResources = projectResources;
            DependencySpecs = dependencySpecs;
            HasDependencies = hasDependencies;
            EmbeddedScriptPaths = embeddedScriptPaths;
            EmbeddedResourceRelativePaths = embeddedResourceRelativePaths;
            UsesExtractedRoot = usesExtractedRoot;
        }

        internal string EntryRelativePath { get; }
        internal string ProjectResources { get; }
        internal string DependencySpecs { get; }
        internal bool HasDependencies { get; }
        internal string[] EmbeddedScriptPaths { get; }
        internal string[] EmbeddedResourceRelativePaths { get; }
        internal bool UsesExtractedRoot { get; }
    }
}
