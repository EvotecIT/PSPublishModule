using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class RepositoryDiagnosticProcessTests
{
    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_inspection_never_invokes_the_getter_that_initializes_or_migrates_stores(bool existingLegacyStore)
    {
        using var host = new IsolatedRepositoryHost();
        Directory.CreateDirectory(Path.GetDirectoryName(host.RepositoryStore)!);
        File.WriteAllText(host.RepositoryStore, "<Repositories/>");
        var legacyStore = Path.Combine(host.Root, "AppData", "Local", "Microsoft", "Windows", "PowerShell", "PowerShellGet", "PSRepositories.xml");
        const string oldStore = "<Objs><OldRepositoryWithoutScriptSourceLocation/></Objs>";
        if (existingLegacyStore)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyStore)!);
            File.WriteAllText(legacyStore, oldStore);
        }
        var getterMarker = Path.Combine(host.Root, "legacy-getter-called");
        var report = new RepositoryDiagnosticService(new LegacyInspectionRunner(host, getterMarker)).Inspect(
            new RepositoryDiagnosticRequest { RepositoryName = "SyntheticDiagnostics", Tool = PublishTool.PowerShellGet });
        Assert.True(report.Runtime.ModuleAvailable);
        Assert.Equal("2.2.5", report.Runtime.ModuleVersion);
        Assert.Null(report.RepositoryFound);
        Assert.Equal("Unavailable", report.Runtime.SettingsInspection);
        Assert.Contains(report.Findings, finding => finding.Code == "PFREPO003");
        Assert.False(File.Exists(getterMarker));
        Assert.Equal(existingLegacyStore, File.Exists(legacyStore));
        if (existingLegacyStore) Assert.Equal(oldStore, File.ReadAllText(legacyStore));
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspection_is_read_only_on_a_fresh_profile_and_exports_saved_inputs_without_decryption(bool windowsPowerShell)
    {
        using var fixture = new IsolatedRepositoryHost(windowsPowerShell);
        var service = new RepositoryDiagnosticService(fixture);
        var request = new RepositoryDiagnosticRequest { RepositoryName = "SyntheticDiagnostics", RepositoryUri = IsolatedRepositoryHost.FeedUri };
        var empty = service.Inspect(request);
        Assert.True(empty.Runtime.ModuleAvailable);
        if (windowsPowerShell) Assert.StartsWith("5.1.", empty.Runtime.PowerShellVersion);
        Assert.Equal("Complete", empty.Runtime.SettingsInspection);
        Assert.False(File.Exists(fixture.NuGetConfig));
        Assert.False(File.Exists(fixture.RepositoryStore));
        Assert.Contains(empty.ConfigurationFiles, f => f.Status == "Missing");

        const string apiKey = "AQAA_NOT_A_REAL_ENCRYPTED_SECRET";
        const string password = "AQAA_NOT_A_REAL_ENCRYPTED_PASSWORD";
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.NuGetConfig)!);
        var xml = $"""
            <configuration>
              <packageSources><add key="SyntheticDiagnostics" value="{IsolatedRepositoryHost.FeedUri}" /></packageSources>
              <apikeys><add key="{IsolatedRepositoryHost.FeedUri}" value="{apiKey}" /></apikeys>
              <packageSourceCredentials><SyntheticDiagnostics><add key="Username" value="PRIVATE_USER"/><add key="Password" value="{password}"/></SyntheticDiagnostics></packageSourceCredentials>
            </configuration>
            """;
        File.WriteAllText(fixture.NuGetConfig, xml);
        fixture.Environment["NuGetPackageSourceCredentials_SyntheticDiagnostics"] = "Username=ENV_USER;Password=ENV_SECRET";
        var saved = service.Inspect(request);
        var export = JsonSerializer.Serialize(saved);
        Assert.Equal(xml, File.ReadAllText(fixture.NuGetConfig));
        Assert.Equal("Complete", saved.Runtime.SettingsInspection);
        Assert.Contains(saved.Findings, f => f.Code == "PFREPO001");
        Assert.Contains(saved.Findings, f => f.Code == "PFREPO002");
        Assert.Contains(saved.AuthenticationInputs, i => i.Kind == "SourceCredential" && i.Storage == "Encrypted");
        Assert.Equal(Version.Parse(saved.Runtime.NuGetVersion!) >= new Version(7, 6), saved.AuthenticationInputs.Any(i => i.Kind == "ApiKeyEnvironment"));
        foreach (var secret in new[] { apiKey, password, "PRIVATE_USER", "ENV_USER", "ENV_SECRET" }) Assert.DoesNotContain(secret, export);

        // A higher-priority clear leaves the old entry on disk, but removes its effect on publishing.
        var extraDirectory = Path.Combine(Path.GetDirectoryName(fixture.NuGetConfig)!, "config");
        Directory.CreateDirectory(extraDirectory);
        File.WriteAllText(Path.Combine(extraDirectory, "old.config"), $"<configuration><apikeys><add key=\"{IsolatedRepositoryHost.FeedUri}\" value=\"{apiKey}\"/></apikeys></configuration>");
        File.WriteAllText(fixture.NuGetConfig, "<configuration><apikeys><clear/></apikeys></configuration>");
        var cleared = service.Inspect(request);
        Assert.Contains(cleared.AuthenticationInputs, i => i.Kind == "NuGetApiKey" && i.Present == true && i.Effective == false);
        Assert.DoesNotContain(cleared.Findings, f => f.Code == "PFREPO001");

        File.WriteAllText(fixture.NuGetConfig, "<configuration><broken>");
        var malformed = service.Inspect(request);
        Assert.Contains(malformed.ConfigurationFiles, f => f.Status == "Unreadable");
        Assert.NotEqual("Complete", malformed.Runtime.SettingsInspection);
        Assert.Equal("<configuration><broken>", File.ReadAllText(fixture.NuGetConfig));
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_inspection_loads_NuGet_from_the_implementing_publisher_module(bool windowsPowerShell)
    {
        using var host = new IsolatedRepositoryHost(windowsPowerShell);
        var report = new RepositoryDiagnosticService(new FacadeInspectionRunner(host)).Inspect(
            new RepositoryDiagnosticRequest { RepositoryName = "SyntheticDiagnostics", Tool = PublishTool.PowerShellGet });
        Assert.Equal("3.0.0", report.Runtime.ModuleVersion);
        Assert.Equal("Complete", report.Runtime.SettingsInspection);
        Assert.Null(report.RepositoryFound);
        Assert.DoesNotContain(report.Findings, finding => finding.Code == "PFREPO006");
        Assert.False(File.Exists(host.NuGetConfig));
        Assert.False(File.Exists(host.RepositoryStore));
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_registration_store_does_not_claim_repository_absence(bool windowsPowerShell)
    {
        using var host = new IsolatedRepositoryHost(windowsPowerShell);
        var service = new RepositoryDiagnosticService(host);
        foreach (var name in new[] { "PSGallery", "SyntheticDiagnostics" })
        {
            var report = service.Inspect(new RepositoryDiagnosticRequest { RepositoryName = name });
            Assert.True(report.Runtime.ModuleAvailable);
            Assert.Null(report.RepositoryFound);
            Assert.DoesNotContain(report.Findings, finding => finding.Code == "PFREPO006");
            Assert.False(File.Exists(host.RepositoryStore));
            Assert.False(File.Exists(host.NuGetConfig));
        }

        // Prove why absence cannot be inferred: the native getter materializes a default.
        var native = host.RunScript("""
            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            if (@(Get-PSResourceRepository -ErrorAction Stop | Where-Object Name -eq PSGallery).Count -ne 1) {
                throw 'Expected the native default repository'
            }
            """);
        Assert.True(native.ExitCode == 0, native.StdErr);
        Assert.True(File.Exists(host.RepositoryStore));
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_inspection_distinguishes_missing_names_from_unreadable_entries(bool windowsPowerShell)
    {
        using var host = new IsolatedRepositoryHost(windowsPowerShell);
        var registered = host.RunScript("""
            Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop
            Register-PSResourceRepository -Name KnownDiagnostics -Uri 'https://packages.example.test/v3/index.json' -Trusted -ErrorAction Stop
            """);
        Assert.True(registered.ExitCode == 0, registered.StdErr);
        var original = File.ReadAllText(host.RepositoryStore);
        var service = new RepositoryDiagnosticService(host);
        var present = service.Inspect(new RepositoryDiagnosticRequest { RepositoryName = "KnownDiagnostics" });
        Assert.True(present.RepositoryFound);
        Assert.Equal(IsolatedRepositoryHost.FeedUri, present.RegisteredUri);

        var missing = service.Inspect(new RepositoryDiagnosticRequest { RepositoryName = "MissingDiagnostics" });
        Assert.False(missing.RepositoryFound);
        Assert.Contains(missing.Findings, finding => finding.Code == "PFREPO006");
        Assert.Equal(original, File.ReadAllText(host.RepositoryStore));

        Assert.Contains(IsolatedRepositoryHost.FeedUri, original);
        var malformed = original.Replace(IsolatedRepositoryHost.FeedUri, "::malformed-uri::");
        File.WriteAllText(host.RepositoryStore, malformed);
        var unreadable = service.Inspect(new RepositoryDiagnosticRequest { RepositoryName = "KnownDiagnostics" });
        Assert.Null(unreadable.RepositoryFound);
        Assert.DoesNotContain(unreadable.Findings, finding => finding.Code == "PFREPO006");
        Assert.Equal(malformed, File.ReadAllText(host.RepositoryStore));

        // A non-file store is a read failure, not evidence that the named repository is absent.
        File.Delete(host.RepositoryStore);
        Directory.CreateDirectory(host.RepositoryStore);
        var invalidStore = service.Inspect(new RepositoryDiagnosticRequest { RepositoryName = "KnownDiagnostics" });
        Assert.Null(invalidStore.RepositoryFound);
        Assert.DoesNotContain(invalidStore.Findings, finding => finding.Code == "PFREPO006");
        Assert.True(Directory.Exists(host.RepositoryStore));
    }

    /// <summary>Real PowerShell boundary with process-local stores, caches, and synthetic credentials.</summary>
    internal sealed class IsolatedRepositoryHost : IPowerShellRunner, IDisposable
    {
        internal const string FeedUri = "https://packages.example.test/v3/index.json";
        private readonly TemporaryDirectory _temporary = new();
        private readonly PowerShellRunner _runner = new();
        private readonly bool _windowsPowerShell;
        internal string Root => _temporary.Path;
        internal string NuGetConfig => Path.Combine(Root, "AppData", "Roaming", "NuGet", "NuGet.Config");
        internal string RepositoryStore => Path.Combine(Root, "AppData", "Local", "PSResourceGet", "PSResourceRepository.xml");
        internal Dictionary<string, string?> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal IsolatedRepositoryHost(bool windowsPowerShell = false)
        {
            _windowsPowerShell = windowsPowerShell;
            Environment["APPDATA"] = Path.Combine(Root, "AppData", "Roaming");
            Environment["LOCALAPPDATA"] = Path.Combine(Root, "AppData", "Local");
            Environment["USERPROFILE"] = Root;
            Environment["HOME"] = Root;
            Environment["TEMP"] = Path.Combine(Root, "temp");
            Environment["TMP"] = Environment["TEMP"];
            Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache");
            Environment["NUGET_SCRATCH"] = Path.Combine(Root, "nuget-scratch");
            Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(Root, "plugin-cache");
            Environment["NUGET_API_KEY"] = null;
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
                if (entry.Key.ToString()!.StartsWith("NuGetPackageSourceCredentials_", StringComparison.OrdinalIgnoreCase)) Environment[entry.Key.ToString()!] = null;
            Directory.CreateDirectory(Environment["APPDATA"]!);
            Directory.CreateDirectory(Environment["LOCALAPPDATA"]!);
            Directory.CreateDirectory(Environment["TEMP"]!);
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _runner.Run(new PowerShellRunRequest(request.ScriptPath!, request.Arguments, request.Timeout,
                preferPwsh: !_windowsPowerShell, workingDirectory: Root, environmentVariables: Environment,
                executableOverride: _windowsPowerShell ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe") : null));

        internal PowerShellRunResult RunScript(string script, IReadOnlyList<string>? arguments = null)
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(path, script, new UTF8Encoding(true));
            try { return Run(new PowerShellRunRequest(path, arguments ?? Array.Empty<string>(), TimeSpan.FromSeconds(30))); }
            finally { File.Delete(path); }
        }

        public void Dispose() => _temporary.Dispose();
    }

    private sealed class FacadeInspectionRunner(IsolatedRepositoryHost host) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            // Simulate only the facade identity; command metadata and NuGet APIs remain native.
            var script = File.ReadAllText(request.ScriptPath!)
                .Replace("Import-Module $moduleName -ErrorAction Stop", "Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop")
                .Replace("Write-PFRepositoryDiagnostic -Repository $Repository", """
                    function Get-Module {
                        param([string]$Name)
                        [pscustomobject]@{ Version = [version]'3.0.0'; ModuleBase = 'missing-facade-directory'; Path = 'facade.psm1' }
                    }
                    Write-PFRepositoryDiagnostic -Repository $Repository
                    """);
            return host.RunScript(script, request.Arguments);
        }
    }

    private sealed class LegacyInspectionRunner(IsolatedRepositoryHost host, string getterMarker) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            // Intercept the side-effecting native boundary so a regression cannot make endpoint requests.
            var script = File.ReadAllText(request.ScriptPath!)
                .Replace("Import-Module $moduleName -ErrorAction Stop", "Import-Module PowerShellGet -RequiredVersion 2.2.5 -ErrorAction Stop")
                .Replace("Write-PFRepositoryDiagnostic -Repository $Repository", $"function Get-PSRepository {{ [IO.File]::WriteAllText('{getterMarker.Replace("'", "''")}', 'called'); throw 'Unsafe legacy getter'; }}\nWrite-PFRepositoryDiagnostic -Repository $Repository");
            return host.RunScript(script, request.Arguments);
        }
    }
}
