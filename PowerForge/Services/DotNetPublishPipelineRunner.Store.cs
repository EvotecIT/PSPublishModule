using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string? ResolveStoreBuildExecutable(string projectPath)
    {
        var fullPath = Path.GetFullPath(FrameworkCompatibility.NotNullOrWhiteSpace(projectPath, nameof(projectPath)).Trim());
        if (!fullPath.EndsWith(".wapproj", StringComparison.OrdinalIgnoreCase))
            return "dotnet";

        var fromEnv = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var candidate = fromEnv.Trim().Trim('"');
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        var onPath = ResolveOnPath("msbuild.exe");
        if (!string.IsNullOrWhiteSpace(onPath))
            return onPath;

        var fromVsWhere = TryResolveMsBuildFromVsWhere();
        if (!string.IsNullOrWhiteSpace(fromVsWhere))
            return fromVsWhere;

        foreach (var candidate in EnumerateKnownMsBuildPaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private DotNetPublishStorePackageResult BuildStorePackage(
        DotNetPublishPlan plan,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var storePackageId = (step.StorePackageId ?? string.Empty).Trim();
        var target = (step.TargetName ?? string.Empty).Trim();
        var framework = (step.Framework ?? string.Empty).Trim();
        var runtime = (step.Runtime ?? string.Empty).Trim();
        var style = step.Style;

        if (string.IsNullOrWhiteSpace(storePackageId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing StorePackageId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");
        if (string.IsNullOrWhiteSpace(step.StorePackageProjectPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing StorePackageProjectPath.");
        if (string.IsNullOrWhiteSpace(step.StorePackageOutputPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing StorePackageOutputPath.");

        var storePackage = (plan.StorePackages ?? Array.Empty<DotNetPublishStorePackagePlan>())
            .FirstOrDefault(i => string.Equals(i.Id, storePackageId, StringComparison.OrdinalIgnoreCase));
        if (storePackage is null)
            throw new InvalidOperationException($"Store package '{storePackageId}' was not found in the plan.");

        var projectPath = Path.GetFullPath(step.StorePackageProjectPath!);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Store packaging project path not found: {projectPath}", projectPath);

        var outputDir = Path.GetFullPath(step.StorePackageOutputPath!);
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, outputDir, $"Store package '{storePackageId}' output path");

        var defaultAppPackagesDir = Path.Combine(Path.GetDirectoryName(projectPath) ?? plan.ProjectRoot, "AppPackages");
        var searchRoots = new[] { outputDir, defaultAppPackagesDir }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (storePackage.ClearOutput && Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch (IOException ex) { _logger.Warn($"Failed to clear Store output directory '{outputDir}': {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { _logger.Warn($"Failed to clear Store output directory '{outputDir}': {ex.Message}"); }
        }

        Directory.CreateDirectory(outputDir);

        var msbuildProperties = new Dictionary<string, string>(plan.MsBuildProperties, StringComparer.OrdinalIgnoreCase);
        if (storePackage.MsBuildProperties is not null)
        {
            foreach (var kv in storePackage.MsBuildProperties)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                msbuildProperties[kv.Key.Trim()] = kv.Value ?? string.Empty;
            }
        }

        var buildExecutable = ResolveStoreBuildExecutable(projectPath);
        if (string.IsNullOrWhiteSpace(buildExecutable))
        {
            throw new InvalidOperationException(
                $"Store packaging project '{projectPath}' requires MSBuild.exe from Visual Studio or Build Tools, but no suitable installation was found. " +
                "Install the Windows Application Packaging workload or set MSBUILD_EXE_PATH to MSBuild.exe.");
        }

        var useDotNet = string.Equals(buildExecutable, "dotnet", StringComparison.OrdinalIgnoreCase);
        var isWapProject = projectPath.EndsWith(".wapproj", StringComparison.OrdinalIgnoreCase);
        var platform = ResolveStorePlatform(runtime);
        var appInstaller = storePackage.AppInstaller?.Enabled == true ? storePackage.AppInstaller : null;
        msbuildProperties["GenerateAppxPackageOnBuild"] = "true";
        if (!isWapProject)
            msbuildProperties["AppxPackageDir"] = EnsureTrailingDirectorySeparator(outputDir);
        msbuildProperties["UapAppxPackageBuildMode"] = storePackage.BuildMode.ToString();
        msbuildProperties["AppxBundle"] = storePackage.Bundle.ToString();
        msbuildProperties["GenerateAppInstallerFile"] = storePackage.GenerateAppInstaller || appInstaller is not null ? "true" : "false";
        if (appInstaller is not null && !string.IsNullOrWhiteSpace(appInstaller.Uri))
            msbuildProperties["AppInstallerUri"] = appInstaller.Uri!;
        msbuildProperties["Platform"] = platform;
        msbuildProperties["AppxBundlePlatforms"] = platform;
        if (!msbuildProperties.ContainsKey("RuntimeIdentifier"))
            msbuildProperties["RuntimeIdentifier"] = runtime;
        msbuildProperties["PowerForgeStorePackageId"] = storePackageId;
        msbuildProperties["PowerForgeSourceTarget"] = target;
        msbuildProperties["PowerForgeSourceFramework"] = framework;
        msbuildProperties["PowerForgeSourceRuntime"] = runtime;
        msbuildProperties["PowerForgeSourceStyle"] = style.Value.ToString();

        var args = new List<string>();
        if (useDotNet)
        {
            args.AddRange(new[]
            {
                "build",
                projectPath,
                "-c",
                plan.Configuration,
                "--nologo"
            });

            if (!plan.Restore)
                args.Add("--no-restore");
        }
        else
        {
            if (!msbuildProperties.ContainsKey("SelfContained"))
                msbuildProperties["SelfContained"] = style.Value == DotNetPublishStyle.FrameworkDependent ? "false" : "true";
            if (!msbuildProperties.ContainsKey("WindowsAppSDKSelfContained"))
                msbuildProperties["WindowsAppSDKSelfContained"] = style.Value == DotNetPublishStyle.FrameworkDependent ? "false" : "true";

            args.Add(projectPath);
            args.Add("/t:Build");
            args.Add("/nologo");
            args.Add($"/p:Configuration={plan.Configuration}");
            if (plan.Restore)
                args.Add("/restore");
        }

        args.AddRange(BuildMsBuildPropertyArgs(msbuildProperties));

        _logger.Info(
            $"Store package build starting for '{storePackageId}' ({target}, {framework}, {runtime}, {style.Value}) -> {Path.GetFileName(projectPath)} using {Path.GetFileName(buildExecutable)}");

        var preBuildFiles = storePackage.ClearOutput ? SnapshotStoreFiles(searchRoots) : null;
        var result = RunProcess(buildExecutable!, plan.ProjectRoot, args);
        if (result.ExitCode != 0)
        {
            var stderr = (result.StdErr ?? string.Empty).TrimEnd();
            var stdout = (result.StdOut ?? string.Empty).TrimEnd();
            var stderrTail = TailLines(stderr, maxLines: 80, maxChars: 8000);
            var stdoutTail = TailLines(stdout, maxLines: 80, maxChars: 8000);
            var message = ExtractLastNonEmptyLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
            if (string.IsNullOrWhiteSpace(message))
                message = $"{Path.GetFileName(buildExecutable)} failed while building Store packaging project.";

            throw new DotNetPublishCommandException(
                message: message,
                fileName: buildExecutable!,
                workingDirectory: string.IsNullOrWhiteSpace(plan.ProjectRoot) ? Environment.CurrentDirectory : plan.ProjectRoot,
                args: args,
                exitCode: result.ExitCode,
                stdOut: stdout,
                stdErr: stderr);
        }

        if (_logger.IsVerbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut))
                _logger.Verbose(result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                _logger.Verbose(result.StdErr.TrimEnd());
        }

        var packageFiles = EnumerateCurrentStoreFiles(searchRoots, preBuildFiles, ".msix", ".msixbundle", ".appx", ".appxbundle");
        var uploadFiles = EnumerateCurrentStoreFiles(searchRoots, preBuildFiles, ".msixupload", ".appxupload");
        var symbolFiles = EnumerateCurrentStoreFiles(searchRoots, preBuildFiles, ".appxsym", ".msixsym");
        var appInstallerFiles = EnumerateCurrentStoreFiles(searchRoots, preBuildFiles, ".appinstaller");
        if (appInstaller is not null)
        {
            foreach (var appInstallerFile in appInstallerFiles)
                NormalizeAppInstallerFile(appInstallerFile, appInstaller);
        }
        var detectedOutputDir = DetermineStoreOutputDir(outputDir, packageFiles, uploadFiles, symbolFiles);

        if (packageFiles.Length == 0 && uploadFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"Store package build for '{storePackageId}' completed, but no *.msix/*.appx/*.msixupload outputs were detected under '{outputDir}' or '{defaultAppPackagesDir}'.");
        }

        _logger.Info(
            $"Store package '{storePackageId}' produced {packageFiles.Length} package file(s), {uploadFiles.Length} upload file(s), {symbolFiles.Length} symbol file(s), {appInstallerFiles.Length} appinstaller file(s).");

        return new DotNetPublishStorePackageResult
        {
            StorePackageId = storePackageId,
            Target = target,
            Framework = framework,
            Runtime = runtime,
            Style = style.Value,
            ProjectPath = projectPath,
            OutputDir = detectedOutputDir,
            OutputFiles = packageFiles,
            UploadFiles = uploadFiles,
            SymbolFiles = symbolFiles,
            AppInstallerFiles = appInstallerFiles
        };
    }

    internal static void NormalizeAppInstallerFile(string path, DotNetPublishAppInstallerOptions options)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        if (options is null || !options.Enabled)
            return;

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
            return;

        var ns = ResolveAppInstallerNamespace(options);
        RewriteElementNamespace(root, ns);
        if (!string.IsNullOrWhiteSpace(options.Uri))
            root.SetAttributeValue("Uri", options.Uri!.Trim());

        var updateSettings = root.Element(ns + "UpdateSettings");
        if (RequiresUpdateSettings(options))
        {
            updateSettings ??= new XElement(ns + "UpdateSettings");
            if (updateSettings.Parent is null)
                root.Add(updateSettings);

            if (options.HoursBetweenUpdateChecks.HasValue ||
                options.ShowPrompt.HasValue ||
                options.UpdateBlocksActivation.HasValue)
            {
                var onLaunch = updateSettings.Element(ns + "OnLaunch");
                onLaunch ??= new XElement(ns + "OnLaunch");
                if (onLaunch.Parent is null)
                    updateSettings.Add(onLaunch);

                if (options.HoursBetweenUpdateChecks.HasValue)
                    onLaunch.SetAttributeValue("HoursBetweenUpdateChecks", options.HoursBetweenUpdateChecks.Value);
                if (options.ShowPrompt.HasValue)
                    onLaunch.SetAttributeValue("ShowPrompt", ToXmlBool(options.ShowPrompt.Value));
                if (options.UpdateBlocksActivation.HasValue)
                    onLaunch.SetAttributeValue("UpdateBlocksActivation", ToXmlBool(options.UpdateBlocksActivation.Value));
            }

            if (options.AutomaticBackgroundTask && updateSettings.Element(ns + "AutomaticBackgroundTask") is null)
                updateSettings.Add(new XElement(ns + "AutomaticBackgroundTask"));

            if (options.ForceUpdateFromAnyVersion.HasValue)
            {
                var forceNs = ResolveForceUpdateNamespace(root, ns, options);
                var force = updateSettings.Element(forceNs + "ForceUpdateFromAnyVersion");
                force ??= new XElement(forceNs + "ForceUpdateFromAnyVersion");
                force.Value = ToXmlBool(options.ForceUpdateFromAnyVersion.Value);
                if (force.Parent is null)
                    updateSettings.Add(force);
            }
        }

        if (options.UpdateUris is { Length: > 0 })
        {
            root.Elements(ns + "UpdateUris").Remove();
            root.Add(new XElement(
                ns + "UpdateUris",
                options.UpdateUris
                    .Where(uri => !string.IsNullOrWhiteSpace(uri))
                    .Select(uri => new XElement(ns + "UpdateUri", uri.Trim()))));
        }

        document.Save(path);
    }

    private static bool RequiresUpdateSettings(DotNetPublishAppInstallerOptions options)
        => options.HoursBetweenUpdateChecks.HasValue ||
           options.ShowPrompt.HasValue ||
           options.UpdateBlocksActivation.HasValue ||
           options.AutomaticBackgroundTask ||
           options.ForceUpdateFromAnyVersion.HasValue;

    private static XNamespace ResolveAppInstallerNamespace(DotNetPublishAppInstallerOptions options)
        => string.Equals(options.SchemaVersion, "2021", StringComparison.OrdinalIgnoreCase)
            ? "http://schemas.microsoft.com/appx/appinstaller/2021"
            : "http://schemas.microsoft.com/appx/appinstaller/2017/2";

    private static XNamespace ResolveForceUpdateNamespace(
        XElement root,
        XNamespace appInstallerNamespace,
        DotNetPublishAppInstallerOptions options)
    {
        if (string.Equals(options.SchemaVersion, "2021", StringComparison.OrdinalIgnoreCase))
            return appInstallerNamespace;

        XNamespace s4 = "http://schemas.microsoft.com/appx/appinstaller/2021";
        root.SetAttributeValue(XNamespace.Xmlns + "s4", s4.NamespaceName);
        return s4;
    }

    private static void RewriteElementNamespace(XElement element, XNamespace ns)
    {
        element.Name = ns + element.Name.LocalName;
        element.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration)
            .Remove();
        foreach (var child in element.Elements())
            RewriteElementNamespace(child, ns);
    }

    private static string ToXmlBool(bool value) => value ? "true" : "false";

    internal static string DetermineStoreOutputDir(string preferredRoot, params string[][] fileSets)
    {
        var normalizedPreferredRoot = Path.GetFullPath(preferredRoot);
        var roots = (fileSets ?? Array.Empty<string[]>())
            .SelectMany(files => files ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetDirectoryName(path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roots.Length == 0)
            return normalizedPreferredRoot;
        if (roots.Any(path => string.Equals(path, normalizedPreferredRoot, StringComparison.OrdinalIgnoreCase)))
            return normalizedPreferredRoot;
        if (roots.Length == 1)
            return roots[0];

        var commonRoot = FindCommonDirectory(roots);
        return string.IsNullOrWhiteSpace(commonRoot) ? normalizedPreferredRoot : commonRoot!;
    }

    private static string[] EnumerateCurrentStoreFiles(
        IEnumerable<string> roots,
        IReadOnlyDictionary<string, StoreFileSnapshot>? preBuildFiles,
        params string[] extensions)
        => EnumerateStoreFiles(roots, extensions)
            .Where(path => preBuildFiles is null || IsNewOrChangedStoreFile(path, preBuildFiles))
            .ToArray();

    private static Dictionary<string, StoreFileSnapshot> SnapshotStoreFiles(IEnumerable<string> roots)
        => EnumerateStoreFiles(roots, ".msix", ".msixbundle", ".appx", ".appxbundle", ".msixupload", ".appxupload", ".appxsym", ".msixsym", ".appinstaller")
            .ToDictionary(
                path => path,
                path => new StoreFileSnapshot(File.GetLastWriteTimeUtc(path), new FileInfo(path).Length),
                StringComparer.OrdinalIgnoreCase);

    private static bool IsNewOrChangedStoreFile(
        string path,
        IReadOnlyDictionary<string, StoreFileSnapshot> preBuildFiles)
    {
        if (!preBuildFiles.TryGetValue(path, out var snapshot))
            return true;

        var info = new FileInfo(path);
        return info.Length != snapshot.Length || info.LastWriteTimeUtc != snapshot.LastWriteTimeUtc;
    }

    private static string[] EnumerateStoreFiles(IEnumerable<string> roots, params string[] extensions)
    {
        var rootList = (roots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (rootList.Length == 0)
            return Array.Empty<string>();

        var extensionSet = new HashSet<string>(
            (extensions ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.StartsWith(".") ? value : "." + value),
            StringComparer.OrdinalIgnoreCase);
        if (extensionSet.Count == 0)
            return Array.Empty<string>();

        return rootList
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => !string.IsNullOrWhiteSpace(path) && extensionSet.Contains(Path.GetExtension(path)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private readonly struct StoreFileSnapshot
    {
        internal StoreFileSnapshot(DateTime lastWriteTimeUtc, long length)
        {
            LastWriteTimeUtc = lastWriteTimeUtc;
            Length = length;
        }

        internal DateTime LastWriteTimeUtc { get; }

        internal long Length { get; }
    }

    private static string ResolveStorePlatform(string runtime)
    {
        var rid = (runtime ?? string.Empty).Trim();
        if (rid.EndsWith("x64", StringComparison.OrdinalIgnoreCase))
            return "x64";
        if (rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase))
            return "ARM64";
        if (rid.EndsWith("x86", StringComparison.OrdinalIgnoreCase))
            return "x86";
        if (rid.EndsWith("arm", StringComparison.OrdinalIgnoreCase))
            return "ARM";
        throw new InvalidOperationException(
            $"Store packaging runtime '{runtime}' does not map to a supported Store platform. Expected a RID ending in x86, x64, arm, or arm64.");
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return fullPath;

        return fullPath + Path.DirectorySeparatorChar;
    }

    private static string? TryResolveMsBuildFromVsWhere()
    {
        try
        {
            var installerRoot = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrWhiteSpace(installerRoot))
                return null;

            var vsWherePath = Path.Combine(installerRoot, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vsWherePath))
                return null;

            var result = RunProcess(vsWherePath, Environment.CurrentDirectory, new[]
            {
                "-latest",
                "-prerelease",
                "-products",
                "*",
                "-requires",
                "Microsoft.Component.MSBuild",
                "-find",
                @"MSBuild\**\Bin\MSBuild.exe"
            });

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
                return null;

            var candidates = (result.StdOut ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim().Trim('"'))
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}amd64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return candidates.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindCommonDirectory(string[] roots)
    {
        if (roots is null || roots.Length == 0)
            return null;

        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .ToArray();
        if (normalizedRoots.Length == 0)
            return null;

        var rootPrefix = Path.GetPathRoot(normalizedRoots[0]) ?? string.Empty;
        if (normalizedRoots.Any(root => !string.Equals(Path.GetPathRoot(root) ?? string.Empty, rootPrefix, StringComparison.OrdinalIgnoreCase)))
            return null;

        var first = SplitRelativePathSegments(normalizedRoots[0], rootPrefix);

        var commonLength = first.Length;
        foreach (var root in normalizedRoots.Skip(1))
        {
            var segments = SplitRelativePathSegments(root, rootPrefix);

            commonLength = Math.Min(commonLength, segments.Length);
            for (var i = 0; i < commonLength; i++)
            {
                if (!string.Equals(first[i], segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    commonLength = i;
                    break;
                }
            }
        }

        if (commonLength == 0)
            return null;

        return Path.Combine(new[] { rootPrefix }.Concat(first.Take(commonLength)).ToArray());
    }

    private static string[] SplitRelativePathSegments(string path, string rootPrefix)
    {
        var relativePath = string.IsNullOrEmpty(rootPrefix) || path.Length < rootPrefix.Length
            ? path
            : path.Substring(rootPrefix.Length);

        return relativePath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateKnownMsBuildPaths()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var editions = new[] { "Insiders", "Preview", "BuildTools", "Enterprise", "Professional", "Community" };
        var versions = new[] { "2022", "2019" };

        foreach (var root in roots)
        {
            foreach (var version in versions)
            {
                foreach (var edition in editions)
                {
                    yield return Path.Combine(root!, "Microsoft Visual Studio", version, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    yield return Path.Combine(root!, "Microsoft Visual Studio", version, edition, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe");
                }
            }
        }
    }
}
