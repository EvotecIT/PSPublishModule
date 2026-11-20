using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private const int MaximumWorkspacePinFileBytes = 16 * 1024 * 1024;

    private static readonly string[] WindowsRuntimeFamilies =
    {
        "Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"
    };

    private static readonly string[] WindowsPackFamilies =
    {
        "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref", "Microsoft.WindowsDesktop.App.Ref",
        "Microsoft.NETCore.App.Host.win-x64", "Microsoft.NETCore.App.Host.win-x86", "Microsoft.NETCore.App.Host.win-arm64"
    };

    private static readonly HashSet<string> SdkVersionAttributes = new(StringComparer.Ordinal)
    {
        "DefaultRuntimeFrameworkVersion", "LatestRuntimeFrameworkVersion", "TargetingPackVersion", "AppHostPackVersion"
    };

    private static readonly HashSet<string> WindowsGeneratedWorkspaceDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "obj", "node_modules", "TestResults", "Artefacts", MaintenanceQuarantine
    };

    private static readonly HashSet<string> WindowsWorkspacePinExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj", ".proj", ".props", ".targets", ".ps1", ".psd1", ".psm1", ".yml", ".yaml"
    };

    private static readonly Regex ToolVersionPattern = new(
        @"(?<![0-9A-Za-z.])\d+\.\d+\.\d+(?![0-9A-Za-z.-])", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static IEnumerable<string> WindowsToolRetentionRoots(RunnerWindowsToolRetention tools)
    {
        foreach (var family in WindowsRuntimeFamilies) yield return Path.Combine(tools.DotNetRoot, "shared", family);
        foreach (var family in WindowsPackFamilies) yield return Path.Combine(tools.DotNetRoot, "packs", family);
        foreach (var root in tools.PSPublishModuleRoots) yield return root;
    }

    private static void ValidateWindowsToolRetention(RunnerMaintenanceSpec spec, RunnerWindowsToolRetention tools)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsFullWindowsDrivePath(tools.DotNetRoot) ||
            PathsEqual(tools.DotNetRoot, Path.GetPathRoot(tools.DotNetRoot)!) || tools.MinimumAgeDays < 1 ||
            tools.PSPublishModuleRoots.Any(path => !IsFullWindowsDrivePath(path) ||
                !string.Equals(Path.GetFileName(path), "PSPublishModule", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Windows tool retention requires physical installation roots and a positive age.");

        var runnerRoots = spec.Runners.SelectMany(r => new[] { r.RootPath, r.WorkRootPath, r.TempPath })
            .Concat(new[] { spec.StateRootPath }).ToArray();
        foreach (var path in tools.PSPublishModuleRoots.Concat(new[] { tools.DotNetRoot }))
        {
            if (runnerRoots.Any(root => PathsEqual(root, path) || MaintenanceContains(root, path) || MaintenanceContains(path, root)))
                throw new ArgumentException("Build-tool retention roots must be separate from runner and maintenance directories.");
            MaintenancePhysicalPath(path);
        }
        if (tools.PSPublishModuleRoots.Any(path => MaintenanceContains(tools.DotNetRoot, path)))
            throw new ArgumentException("The module root must be outside the .NET installation.");
    }

    private static bool IsFullWindowsDrivePath(string path)
        => !string.IsNullOrWhiteSpace(path) && Path.GetPathRoot(path) is { Length: 3 } &&
           path[1] == ':' && (path[2] == '\\' || path[2] == '/') &&
           string.Equals(Path.GetFullPath(path), path.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private void MaintenanceWindowsToolPhase(MaintenanceRun run, RunnerWindowsToolRetention tools)
    {
        var elapsed = run.Clock.Elapsed;
        var remaining = TimeSpan.FromSeconds(run.Spec.MaximumRunSeconds) - elapsed;
        if (remaining <= TimeSpan.Zero) return;
        // Reserve the other half of the remaining time for existing runner and cache maintenance.
        run.PhaseDeadline = elapsed + TimeSpan.FromTicks(remaining.Ticks / 2);
        try
        {
            MaintenanceRecover(run, recoverWindowsTools: true);
            MaintenanceRetainOnIo(run, "Windows build tooling", () => MaintenanceWindowsTools(run, tools));
            MaintenanceDeleteIsolated(run);
        }
        finally
        {
            if (run.Expired)
            {
                run.Result.Deferred = true;
                run.Retained.Add("Windows tool retention deferred by its execution budget.");
            }
            run.State.Due |= run.Result.Deferred || MaintenanceHasPendingJournals(run);
            run.Isolated.Clear();
            run.PhaseDeadline = null;
        }
    }

    private static void MaintenanceWindowsTools(MaintenanceRun run, RunnerWindowsToolRetention tools)
    {
        var cutoff = DateTime.UtcNow.AddDays(-tools.MinimumAgeDays);
        var workspacePins = WindowsWorkspaceVersionPins(run);
        var dotNetPins = new HashSet<string>(tools.ProtectedDotNetVersions, StringComparer.OrdinalIgnoreCase);
        dotNetPins.UnionWith(workspacePins);
        var modulePins = new HashSet<string>(tools.ProtectedPSPublishModuleVersions, StringComparer.OrdinalIgnoreCase);
        modulePins.UnionWith(workspacePins);

        // A newer patch can be installed alongside a registered bundle. Directory retirement must
        // never remove the files that Windows Installer owns or a retained SDK references.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                dotNetPins.UnionWith(WindowsRegisteredDotNetVersions(run));
                dotNetPins.UnionWith(WindowsSdkVersionReferences(run, tools.DotNetRoot));
                foreach (var root in WindowsToolRetentionRoots(tools).Take(WindowsRuntimeFamilies.Length + WindowsPackFamilies.Length))
                    RetireWindowsToolFamily(run, root, dotNetPins, cutoff, keep: 2, perChannel: true, WindowsDotNetLayout);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is JsonException || error is XmlException || error is RegexMatchTimeoutException ||
                error is System.Security.SecurityException)
            {
                run.Retained.Add(".NET tools retained because installation ownership or SDK requirements could not be read — " + error.GetType().Name);
            }
        }
        foreach (var root in tools.PSPublishModuleRoots)
            RetireWindowsToolFamily(run, root, modulePins, cutoff, keep: 3, perChannel: false, WindowsGalleryModuleLayout);
    }

    private static HashSet<string> WindowsWorkspaceVersionPins(MaintenanceRun run)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in run.State.Jobs.Select(job => job.Workspace).Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(GetPathStringComparer()))
        {
            if (!Directory.Exists(workspace) || !run.Spec.Runners.Any(r => MaintenanceContains(r.WorkRootPath, workspace))) continue;
            var buildRoots = new[] { Path.Combine(workspace, ".github", "workflows"), Path.Combine(workspace, ".github", "actions"),
                Path.Combine(workspace, "Build") };
            foreach (var file in WindowsWorkspaceFiles(run, workspace))
            {
                if (run.Expired) throw new MaintenanceBudgetExpiredException();
                var extension = Path.GetExtension(file);
                var name = Path.GetFileName(file);
                if (!WindowsWorkspacePinExtensions.Contains(extension) &&
                    !name.Equals("global.json", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("runtimeconfig.template.json", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) &&
                    !(extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                        buildRoots.Any(root => MaintenanceContains(root, file)))) continue;
                if (!File.Exists(file)) continue;
                MaintenancePhysicalPath(file);
                if (new FileInfo(file).Length > MaximumWorkspacePinFileBytes)
                    throw new IOException("A version-pinning file exceeded the scan limit: " + name);
                try { AddToolVersionMatches(result, File.ReadAllText(file)); }
                catch (RegexMatchTimeoutException error) { throw new IOException("Version pin scanning timed out: " + name, error); }
            }
            if (run.Expired) throw new MaintenanceBudgetExpiredException();
        }
        return result;
    }

    private static IEnumerable<string> WindowsWorkspaceFiles(MaintenanceRun run, string workspace)
    {
        MaintenancePhysicalPath(workspace);
        var gitMetadata = Path.Combine(workspace, ".git");
        if (!Directory.Exists(gitMetadata) && !File.Exists(gitMetadata))
            return MaintenanceDirectories(workspace, run, includeGenerated: true,
                excludeDirectory: WindowsGeneratedWorkspaceDirectories.Contains).SelectMany(Directory.EnumerateFiles);

        MaintenancePhysicalPath(gitMetadata);
        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        if (!File.Exists(git)) throw new IOException("Git for Windows is unavailable for checkout pin discovery.");
        MaintenancePhysicalPath(git);
        var deadline = run.PhaseDeadline ?? TimeSpan.FromSeconds(run.Spec.MaximumRunSeconds);
        var remaining = deadline - run.Clock.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new MaintenanceBudgetExpiredException();
        var arguments = new[] { "-c", "core.fsmonitor=false", "-c", "core.hooksPath=NUL", "-c",
            "safe.directory=" + workspace.Replace('\\', '/'), "--no-optional-locks", "ls-files", "-z", "--cached", "--recurse-submodules" };
        var request = new ProcessRunRequest(git, workspace, arguments, remaining)
        {
            MaxCapturedOutputCharacters = 8 * 1024 * 1024
        };
        var listing = new ProcessRunner(ownProcessTree: true).RunAsync(request).GetAwaiter().GetResult();
        if (!listing.Succeeded) throw new IOException("Unable to read the enrolled Git checkout's tracked files.");
        return listing.StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(relative => Path.GetFullPath(Path.Combine(workspace, relative)))
            .Select(path => MaintenanceContains(workspace, path) ? path : throw new IOException("A tracked pin path escaped its workspace."));
    }

    private static void RetireWindowsToolFamily(MaintenanceRun run, string root, ISet<string> pins, DateTime cutoff,
        int keep, bool perChannel, Func<string, string, bool> hasExpectedLayout)
    {
        if (run.Expired || !Directory.Exists(root)) return;
        MaintenancePhysicalPath(root);
        var versions = Directory.EnumerateDirectories(root).Select(path =>
        {
            var name = Path.GetFileName(path);
            return (Path: path, Name: name, Version: Version.TryParse(name, out var parsed) && parsed.Build >= 0 &&
                parsed.Revision < 0 && parsed.ToString(3) == name ? parsed : null);
        }).Where(item => item.Version is not null).ToArray();
        // Preserve the highest stable versions within every runtime/pack channel or module major.
        foreach (var group in versions.GroupBy(item => (item.Version!.Major, Minor: perChannel ? item.Version.Minor : 0)))
        {
            var verified = new List<string>();
            foreach (var item in group.OrderByDescending(item => item.Version))
            {
                if (run.Expired) return;
                if (!hasExpectedLayout(item.Path, item.Name)) continue;
                verified.Add(item.Path);
                if (verified.Count <= keep || pins.Contains(item.Name) ||
                    Directory.GetCreationTimeUtc(item.Path) >= cutoff || Directory.GetLastWriteTimeUtc(item.Path) >= cutoff ||
                    run.ExecutablePaths.Any(executable => MaintenanceContains(item.Path, executable)))
                    continue;
                MaintenanceIsolate(run, item.Path, root, directory: true, olderThan: cutoff, windowsTool: true);
            }
        }
    }

    private static IEnumerable<string> WindowsSdkVersionReferences(MaintenanceRun run, string root)
    {
        MaintenancePhysicalPath(root);
        if (!File.Exists(Path.Combine(root, "dotnet.exe"))) throw new IOException("The .NET muxer is missing.");
        var sdkRoot = Path.Combine(root, "sdk");
        MaintenancePhysicalPath(sdkRoot);
        var sdkDirectories = Directory.GetDirectories(sdkRoot);
        if (sdkDirectories.Length == 0) throw new IOException("No installed SDK was found.");
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sdk in sdkDirectories)
        {
            if (run.Expired) throw new MaintenanceBudgetExpiredException();
            if (Path.GetFileName(sdk).Equals(".powerforge-quarantine", StringComparison.OrdinalIgnoreCase)) continue;
            MaintenancePhysicalPath(sdk);
            var runtime = Path.Combine(sdk, "dotnet.runtimeconfig.json");
            var bundled = Path.Combine(sdk, "Microsoft.NETCoreSdk.BundledVersions.props");
            if (!File.Exists(runtime) && !File.Exists(bundled) &&
                Directory.EnumerateFileSystemEntries(sdk).All(entry => Path.GetFileName(entry).Equals("Roslyn", StringComparison.OrdinalIgnoreCase)))
                continue;
            MaintenancePhysicalPath(runtime);
            MaintenancePhysicalPath(bundled);
            if (new FileInfo(runtime).Length > 1024 * 1024 || new FileInfo(bundled).Length > 1024 * 1024)
                throw new IOException("Installed SDK version metadata exceeded the scan limit.");
            using var json = JsonDocument.Parse(File.ReadAllText(runtime));
            var document = json.RootElement;
            if (document.ValueKind != JsonValueKind.Object ||
                !document.TryGetProperty("runtimeOptions", out var options) || options.ValueKind != JsonValueKind.Object ||
                !options.TryGetProperty("framework", out var framework) || framework.ValueKind != JsonValueKind.Object ||
                !framework.TryGetProperty("version", out var sdkRuntime) || sdkRuntime.ValueKind != JsonValueKind.String ||
                sdkRuntime.GetString() is not { Length: > 0 } version)
                throw new IOException("An installed SDK's runtime requirement could not be read.");
            versions.Add(version);
            using var reader = XmlReader.Create(bundled, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var props = XDocument.Load(reader);
            foreach (var attribute in props.Descendants().Attributes().Where(a => SdkVersionAttributes.Contains(a.Name.LocalName)))
                versions.Add(attribute.Value);
        }
        if (versions.Count == 0) throw new IOException("No SDK version metadata was read.");
        return versions;
    }

#if NET8_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    private static IEnumerable<string> WindowsRegisteredDotNetVersions(MaintenanceRun run)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) throw new IOException("Windows Installer registrations could not be read.");
            foreach (var name in uninstall.GetSubKeyNames())
            {
                if (run.Expired) throw new MaintenanceBudgetExpiredException();
                using var key = uninstall.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string display) continue;
                if (display.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) &&
                    (display.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     display.IndexOf("Windows Desktop Runtime", StringComparison.OrdinalIgnoreCase) >= 0))
                    AddToolVersionMatches(result, display);
            }
            foreach (var architecture in new[] { "x64", "x86", "arm64" })
            {
                if (run.Expired) throw new MaintenanceBudgetExpiredException();
                using var shared = machine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\" + architecture + @"\sharedfx");
                if (shared is null) continue;
                foreach (var family in shared.GetSubKeyNames())
                {
                    using var key = shared.OpenSubKey(family);
                    if (key is not null) result.UnionWith(key.GetValueNames());
                }
            }
        }
        return result;
    }

    private static bool WindowsDotNetLayout(string path, string version)
    {
        MaintenancePhysicalPath(path);
        var family = Path.GetFileName(Path.GetDirectoryName(path)!);
        var marker = family.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(path, "data", "FrameworkList.xml")
            : family.StartsWith("Microsoft.NETCore.App.Host.", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(path, "runtimes", family.Substring("Microsoft.NETCore.App.Host.".Length), "native", "apphost.exe")
                : Path.Combine(path, family + ".deps.json");
        MaintenancePhysicalPath(marker);
        return File.Exists(marker);
    }

    private static bool WindowsGalleryModuleLayout(string path, string version)
    {
        MaintenancePhysicalPath(path);
        var info = Path.Combine(path, "PSGetModuleInfo.xml");
        var manifest = Path.Combine(path, "PSPublishModule.psd1");
        MaintenancePhysicalPath(info);
        MaintenancePhysicalPath(manifest);
        if (!File.Exists(info) || !File.Exists(manifest) ||
            new FileInfo(info).Length > 1024 * 1024 || new FileInfo(manifest).Length > 1024 * 1024) return false;
        var metadata = File.ReadAllText(info);
        return metadata.IndexOf("https://www.powershellgallery.com/api/v2", StringComparison.OrdinalIgnoreCase) >= 0 &&
            metadata.IndexOf("PSPublishModule", StringComparison.OrdinalIgnoreCase) >= 0 &&
            File.ReadLines(manifest).Any(line => WindowsModuleVersionLineMatches(line, version));
    }

    private static bool WindowsModuleVersionLineMatches(string line, string version)
    {
        var separator = line.IndexOf('=');
        if (separator < 0 || !line.Substring(0, separator).Trim().Equals("ModuleVersion", StringComparison.OrdinalIgnoreCase))
            return false;
        var value = line.Substring(separator + 1).TrimStart();
        return value.StartsWith("'" + version + "'", StringComparison.Ordinal) ||
            value.StartsWith("\"" + version + "\"", StringComparison.Ordinal);
    }

    private static void AddToolVersionMatches(ISet<string> result, string text)
    {
        foreach (Match match in ToolVersionPattern.Matches(text)) result.Add(match.Value);
    }
}
