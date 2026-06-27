namespace PowerForge;

internal sealed class ManagedModuleNativeCompatibilityBenchmarkRunner : IManagedModuleNativeCompatibilityBenchmarkRunner
{
    private static readonly char PathSeparator = Path.PathSeparator;
    private readonly IPowerShellRunner _runner;

    internal ManagedModuleNativeCompatibilityBenchmarkRunner(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public void Prepare(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine)
    {
        if (scenario.Operation != ManagedModuleBenchmarkOperation.Update)
            return;

        var sandboxRoot = ResolveSandboxRoot(scenario);
        var previous = FindInstalledVersion(sandboxRoot, scenario.Name);
        if (string.IsNullOrWhiteSpace(previous))
            return;

        var repositoryName = ResolveRepositoryName(scenario);
        var nativeRunner = CreateRunner(engine, sandboxRoot);
        EnsureRepository(engine, scenario, repositoryName, nativeRunner);
        RemoveCopiedInstalledModule(sandboxRoot, scenario.Name);
        RunInstall(
            CloneWithVersion(scenario, previous!),
            engine,
            repositoryName,
            nativeRunner);
    }

    public ModuleDependencyInstallResult Run(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine)
    {
        if (scenario is null)
            throw new ArgumentNullException(nameof(scenario));
        if (scenario.Operation is not ManagedModuleBenchmarkOperation.Install and not ManagedModuleBenchmarkOperation.Update)
            throw new InvalidOperationException("Native compatibility benchmarks only support install and update operations.");

        var sandboxRoot = ResolveSandboxRoot(scenario);
        Directory.CreateDirectory(sandboxRoot);

        var previous = FindInstalledVersion(sandboxRoot, scenario.Name);
        var repositoryName = ResolveRepositoryName(scenario);
        var nativeRunner = CreateRunner(engine, sandboxRoot);
        EnsureRepository(engine, scenario, repositoryName, nativeRunner);

        var status = scenario.Operation == ManagedModuleBenchmarkOperation.Update
            ? RunUpdate(scenario, engine, repositoryName, nativeRunner)
            : RunInstall(scenario, engine, repositoryName, nativeRunner);
        var resolved = FindInstalledVersion(sandboxRoot, scenario.Name) ?? scenario.Version ?? scenario.MinimumVersion;

        return new ModuleDependencyInstallResult(
            scenario.Name,
            previous,
            resolved,
            scenario.Version ?? scenario.MinimumVersion,
            status,
            engine.ToString(),
            null);
    }

    private ModuleDependencyInstallStatus RunInstall(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        string? repositoryName,
        IPowerShellRunner nativeRunner)
    {
        if (engine == ManagedModuleBenchmarkEngine.PSResourceGet)
        {
            new PSResourceGetClient(nativeRunner, new NullLogger()).Install(
                new PSResourceInstallOptions(
                    scenario.Name,
                    BuildPSResourceVersionArgument(scenario),
                    repositoryName,
                    scope: "CurrentUser",
                    prerelease: scenario.IncludePrerelease,
                    reinstall: scenario.Force,
                    trustRepository: true,
                    skipDependencyCheck: scenario.SkipDependencyCheck,
                    acceptLicense: scenario.AcceptLicense,
                    quiet: true,
                    credential: scenario.Credential),
                TimeSpan.FromMinutes(10));
            return ModuleDependencyInstallStatus.Installed;
        }

        RunEmbeddedScript(
            nativeRunner,
            "Scripts/ModuleDependencyInstaller/Install-Module.ps1",
            new[]
            {
                scenario.Name,
                scenario.Version ?? string.Empty,
                scenario.MinimumVersion ?? string.Empty,
                repositoryName ?? string.Empty,
                scenario.Credential?.UserName ?? string.Empty,
                scenario.Credential?.Secret ?? string.Empty,
                scenario.AllowClobber ? "1" : "0"
            },
            "Install-Module");
        return ModuleDependencyInstallStatus.Installed;
    }

    private ModuleDependencyInstallStatus RunUpdate(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        string? repositoryName,
        IPowerShellRunner nativeRunner)
    {
        if (engine == ManagedModuleBenchmarkEngine.PSResourceGet)
        {
            RunEmbeddedScript(
                nativeRunner,
                "Scripts/ModuleDependencyInstaller/Update-PSResource.ps1",
                new[]
                {
                    scenario.Name,
                    repositoryName ?? string.Empty,
                    scenario.IncludePrerelease ? "1" : "0",
                    scenario.Credential?.UserName ?? string.Empty,
                    scenario.Credential?.Secret ?? string.Empty
                },
                "Update-PSResource");
            return ModuleDependencyInstallStatus.Updated;
        }

        RunEmbeddedScript(
            nativeRunner,
            "Scripts/ModuleDependencyInstaller/Update-Module.ps1",
            new[]
            {
                scenario.Name,
                scenario.IncludePrerelease ? "1" : "0",
                scenario.Credential?.UserName ?? string.Empty,
                scenario.Credential?.Secret ?? string.Empty
            },
            "Update-Module");
        return ModuleDependencyInstallStatus.Updated;
    }

    private void EnsureRepository(
        ManagedModuleBenchmarkEngine engine,
        ManagedModuleBenchmarkScenario scenario,
        string? repositoryName,
        IPowerShellRunner nativeRunner)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
            return;
        if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            return;

        var source = scenario.Repository.Source;
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (engine == ManagedModuleBenchmarkEngine.PSResourceGet)
        {
            new PSResourceGetClient(nativeRunner, new NullLogger()).EnsureRepositoryRegistered(repositoryName!, source!, trusted: true);
            return;
        }

        new PowerShellGetClient(nativeRunner, new NullLogger()).EnsureRepositoryRegistered(
            repositoryName!,
            source!,
            source!,
            trusted: true,
            credential: scenario.Credential);
    }

    private IPowerShellRunner CreateRunner(ManagedModuleBenchmarkEngine engine, string sandboxRoot)
    {
        var environment = engine == ManagedModuleBenchmarkEngine.PowerShellGet
            ? BuildWindowsPowerShellEnvironment(sandboxRoot)
            : BuildPSResourceGetEnvironment(sandboxRoot);
        var preferPwsh = engine != ManagedModuleBenchmarkEngine.PowerShellGet || !IsWindows();
        var executable = engine == ManagedModuleBenchmarkEngine.PowerShellGet && IsWindows()
            ? "powershell.exe"
            : null;

        return new ManagedModuleNativePowerShellRunner(_runner, environment, sandboxRoot, preferPwsh, executable);
    }

    private static Dictionary<string, string?> BuildPSResourceGetEnvironment(string sandboxRoot)
    {
        var fakeHome = Path.Combine(sandboxRoot, "home");
        var moduleRoot = ResolveCoreCurrentUserModuleRoot(fakeHome);
        CreateProfileDirectories(fakeHome, moduleRoot);

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PSModulePath"] = moduleRoot + PathSeparator + (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty),
            ["HOME"] = fakeHome,
            ["USERPROFILE"] = fakeHome,
            ["APPDATA"] = Path.Combine(fakeHome, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(fakeHome, "AppData", "Local")
        };
    }

    private static Dictionary<string, string?> BuildWindowsPowerShellEnvironment(string sandboxRoot)
    {
        var fakeHome = Path.Combine(sandboxRoot, "home");
        var moduleRoot = Path.Combine(fakeHome, "Documents", "WindowsPowerShell", "Modules");
        CreateProfileDirectories(fakeHome, moduleRoot);

        var paths = new List<string> { moduleRoot };
        AddIfDirectory(paths, Path.Combine(GetDocumentsPath(), "WindowsPowerShell", "Modules"));
        AddIfDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules"));
        AddIfDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules"));

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PSModulePath"] = string.Join(PathSeparator.ToString(), paths),
            ["HOME"] = fakeHome,
            ["USERPROFILE"] = fakeHome,
            ["APPDATA"] = Path.Combine(fakeHome, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(fakeHome, "AppData", "Local")
        };
    }

    private static void RunEmbeddedScript(
        IPowerShellRunner runner,
        string embeddedPath,
        IReadOnlyList<string> arguments,
        string commandName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "managed-module-native");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "native_" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(scriptPath, EmbeddedScripts.Load(embeddedPath), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var result = runner.Run(new PowerShellRunRequest(scriptPath, arguments, TimeSpan.FromMinutes(10), preferPwsh: true));
            if (result.ExitCode != 0)
                throw new InvalidOperationException(commandName + " failed (exit " + result.ExitCode + "). " + ExtractError(result.StdOut, result.StdErr));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static string ResolveSandboxRoot(ManagedModuleBenchmarkScenario scenario)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(scenario.ModuleRoot)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "managed-module-native", Guid.NewGuid().ToString("N"))
            : scenario.ModuleRoot!);

    private static string? ResolveRepositoryName(ManagedModuleBenchmarkScenario scenario)
    {
        var name = scenario.Repository?.Name?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? BuildPSResourceVersionArgument(ManagedModuleBenchmarkScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.Version))
            return scenario.Version!.Trim();
        if (!string.IsNullOrWhiteSpace(scenario.MinimumVersion) || !string.IsNullOrWhiteSpace(scenario.MaximumVersion))
            return ManagedModuleVersionRange.FromBounds(scenario.MinimumVersion, scenario.MaximumVersion).ToString();
        return null;
    }

    private static ManagedModuleBenchmarkScenario CloneWithVersion(
        ManagedModuleBenchmarkScenario scenario,
        string version)
        => new()
        {
            Id = scenario.Id,
            Operation = ManagedModuleBenchmarkOperation.Install,
            Repository = scenario.Repository,
            Name = scenario.Name,
            Version = version,
            MinimumVersion = null,
            MaximumVersion = null,
            VersionPolicy = null,
            IncludePrerelease = scenario.IncludePrerelease,
            Scope = scenario.Scope,
            ShellEdition = scenario.ShellEdition,
            ModuleRoot = scenario.ModuleRoot,
            ModulePath = scenario.ModulePath,
            ManifestPath = scenario.ManifestPath,
            PackageCacheDirectory = scenario.PackageCacheDirectory,
            PackageOutputDirectory = scenario.PackageOutputDirectory,
            Credential = scenario.Credential,
            Force = true,
            AllowClobber = scenario.AllowClobber,
            AcceptLicense = scenario.AcceptLicense,
            SkipDependencyCheck = scenario.SkipDependencyCheck,
            Iterations = scenario.Iterations
        };

    private static string? FindInstalledVersion(string sandboxRoot, string moduleName)
        => FindInstalledManifest(sandboxRoot, moduleName) is { } manifest
            ? ReadManifestVersion(manifest)
            : null;

    private static FileInfo? FindInstalledManifest(string sandboxRoot, string moduleName)
    {
        if (!Directory.Exists(sandboxRoot))
            return null;

        return new DirectoryInfo(sandboxRoot)
            .EnumerateFiles(moduleName + ".psd1", SearchOption.AllDirectories)
            .Where(file => file.FullName.IndexOf(Path.DirectorySeparatorChar + "source" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
            .OrderByDescending(file => ReadManifestVersion(file) ?? string.Empty, ManagedModuleVersionComparer.Instance)
            .FirstOrDefault();
    }

    private static void RemoveCopiedInstalledModule(string sandboxRoot, string moduleName)
    {
        if (!Directory.Exists(sandboxRoot))
            return;

        foreach (var manifest in new DirectoryInfo(sandboxRoot)
                     .EnumerateFiles(moduleName + ".psd1", SearchOption.AllDirectories)
                     .Where(file => file.FullName.IndexOf(Path.DirectorySeparatorChar + "source" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                     .ToArray())
        {
            var versionDirectory = manifest.Directory;
            if (versionDirectory is null)
                continue;

            var moduleDirectory = versionDirectory.Parent is not null &&
                                  string.Equals(versionDirectory.Parent.Name, moduleName, StringComparison.OrdinalIgnoreCase)
                ? versionDirectory.Parent.FullName
                : versionDirectory.FullName;

            if (!IsSameOrChildPath(moduleDirectory, sandboxRoot))
                continue;

            try { Directory.Delete(moduleDirectory, recursive: true); }
            catch { }
        }
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadManifestVersion(FileInfo manifest)
    {
        var version = ModuleManifestValueReader.ReadTopLevelString(manifest.FullName, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifest.FullName, "Prerelease").FirstOrDefault();
        return string.IsNullOrWhiteSpace(prerelease) || version!.IndexOf("-", StringComparison.Ordinal) >= 0
            ? version
            : version + "-" + prerelease;
    }

    private static string ExtractError(string stdout, string stderr)
    {
        foreach (var line in (stdout ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.IndexOf("::ERROR::", StringComparison.Ordinal);
            if (marker < 0)
                continue;

            var value = line.Substring(marker + "::ERROR::".Length);
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return value; }
        }

        return string.IsNullOrWhiteSpace(stderr) ? (stdout ?? string.Empty).Trim() : stderr.Trim();
    }

    private static void CreateProfileDirectories(string fakeHome, string moduleRoot)
    {
        Directory.CreateDirectory(moduleRoot);
        Directory.CreateDirectory(Path.Combine(fakeHome, "AppData", "Roaming"));
        Directory.CreateDirectory(Path.Combine(fakeHome, "AppData", "Local"));
        Directory.CreateDirectory(Path.Combine(fakeHome, "Documents", "PowerShell", "Modules"));
        Directory.CreateDirectory(Path.Combine(fakeHome, "Documents", "WindowsPowerShell", "Modules"));
        Directory.CreateDirectory(Path.Combine(fakeHome, ".local", "share", "powershell", "Modules"));
    }

    private static string ResolveCoreCurrentUserModuleRoot(string fakeHome)
        => IsWindows()
            ? Path.Combine(fakeHome, "Documents", "PowerShell", "Modules")
            : Path.Combine(fakeHome, ".local", "share", "powershell", "Modules");

    private static void AddIfDirectory(List<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            paths.Add(path);
    }

    private static string GetDocumentsPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
            : documents;
    }

    private static bool IsWindows()
        => Path.DirectorySeparatorChar == '\\';
}
