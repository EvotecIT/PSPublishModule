using System.Collections;
using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class ManagedModuleRepositoryParityCommandTests
{
    [Theory]
    [InlineData("Register-ManagedModuleRepository", new[] { "Name", "Uri", "PSGallery", "Repository", "Trusted", "Priority", "ApiVersion", "Force", "PassThru", "Scope" })]
    [InlineData("Reset-ManagedModuleRepository", new[] { "PassThru", "Scope" })]
    [InlineData("Import-ManagedModuleRepository", new[] { "Path", "Force", "PassThru", "Scope" })]
    [InlineData("Unregister-ManagedModuleRepository", new[] { "Name", "PassThru", "Scope" })]
    public void Repository_parity_commands_expose_psresourceget_shaped_parameters(string commandName, string[] parameterNames)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(commandName);

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        foreach (var parameterName in parameterNames)
            Assert.True(command.Parameters.ContainsKey(parameterName), $"{commandName} should expose {parameterName}.");
    }

    [Fact]
    public void RegisterManagedModuleRepository_is_quiet_by_default_and_saves_profile()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("Uri", "https://packages.example.test/nuget/v3/index.json")
            .AddParameter("Trusted")
            .AddParameter("Priority", 25);

        Assert.Empty(ps.Invoke());
        AssertNoPowerShellErrors(ps);

        var profile = Assert.Single(new ModuleRepositoryProfileStore().GetProfiles());
        Assert.Equal("Internal", profile.Name);
        Assert.Equal(PrivateGalleryProvider.NuGet, profile.Provider);
        Assert.Equal("Internal", profile.RepositoryName);
        Assert.Equal("https://packages.example.test/nuget/v3/index.json", profile.RepositoryUri);
        Assert.True(profile.Trusted);
        Assert.Equal(25, profile.Priority);
    }

    [Fact]
    public void RegisterManagedModuleRepository_rejects_existing_profile_without_force()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("Uri", "https://packages.example.test/nuget/v3/index.json");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("Uri", "https://packages.example.test/nuget/v3/index.json");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterManagedModuleRepository_force_replaces_existing_profile()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("Uri", "https://old.example.test/v3/index.json");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("Uri", "https://new.example.test/v3/index.json")
            .AddParameter("Force")
            .AddParameter("PassThru");

        var result = Assert.IsType<ModuleRepositoryProfileResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("https://new.example.test/v3/index.json", result.RepositoryUri);
        Assert.Equal(result.RepositoryUri, Assert.Single(new ModuleRepositoryProfileStore().GetProfiles()).RepositoryUri);
    }

    [Fact]
    public void RegisterManagedModuleRepository_accepts_repository_hashtable()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();
        var repository = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Internal",
            ["Uri"] = "https://packages.example.test/nuget/v3/index.json",
            ["Trusted"] = true,
            ["Priority"] = 15
        };

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Repository", new[] { repository })
            .AddParameter("PassThru");

        var result = Assert.IsType<ModuleRepositoryProfileResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Internal", result.Name);
        Assert.True(result.Trusted);
        Assert.Equal(15, result.Priority);
    }

    [Fact]
    public void RegisterManagedModuleRepository_preserves_api_version_and_absolute_local_feed_path()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "LocalFeed")
            .AddParameter("Uri", "/tmp/powerforge-feed")
            .AddParameter("ApiVersion", RepositoryApiVersion.V2)
            .AddParameter("PassThru");

        var result = Assert.IsType<ModuleRepositoryProfileResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal(PrivateGalleryProvider.NuGet, result.Provider);
        Assert.Equal(RepositoryApiVersion.V2, result.ApiVersion);
        Assert.Equal("/tmp/powerforge-feed", result.RepositoryUri);
        Assert.Equal("/tmp/powerforge-feed", result.RepositorySourceUri);
        Assert.Equal(50, result.Priority);

        var profile = Assert.Single(new ModuleRepositoryProfileStore().GetProfiles());
        Assert.Equal(RepositoryApiVersion.V2, profile.ApiVersion);
        Assert.Equal("/tmp/powerforge-feed", profile.RepositoryUri);
    }

    [Fact]
    public void RegisterManagedModuleRepository_detects_azure_artifacts_url_profiles()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Name", "AzModules")
            .AddParameter("Uri", "https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json")
            .AddParameter("PassThru");

        var result = Assert.IsType<ModuleRepositoryProfileResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal(PrivateGalleryProvider.AzureArtifacts, result.Provider);
        Assert.Equal("contoso", result.AzureDevOpsOrganization);
        Assert.Equal("Platform", result.AzureDevOpsProject);
        Assert.Equal("Modules", result.AzureArtifactsFeed);
        Assert.Equal("AzModules", result.RepositoryName);
        Assert.Equal(PrivateGalleryBootstrapMode.ExistingSession, result.BootstrapMode);
        Assert.Equal("AzureArtifactsCredentialProvider", result.AuthenticationMode);
        Assert.Equal(50, result.Priority);
    }

    [Fact]
    public void RegisterManagedModuleRepository_rejects_repository_hashtable_priority_outside_psresourceget_range()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        using var ps = CreatePowerShellWithModuleImported();
        var repository = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Internal",
            ["Uri"] = "https://packages.example.test/nuget/v3/index.json",
            ["Priority"] = 200
        };

        ps.AddCommand("Register-ManagedModuleRepository")
            .AddParameter("Repository", new[] { repository });

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("priority", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetManagedModuleRepository_replaces_profiles_with_default_psgallery_profile()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Internal",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "Internal",
            RepositoryUri = "https://packages.example.test/nuget/v3/index.json"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Reset-ManagedModuleRepository")
            .AddParameter("PassThru");

        var result = Assert.IsType<ModuleRepositoryProfileResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("PSGallery", result.Name);
        Assert.Equal(ManagedModuleCommandSupport.DefaultRepositorySource, result.RepositoryUri);
        Assert.False(result.Trusted);

        var profile = Assert.Single(new ModuleRepositoryProfileStore().GetProfiles());
        Assert.Equal("PSGallery", profile.Name);
        Assert.Equal(ManagedModuleCommandSupport.DefaultRepositorySource, profile.RepositoryUri);
    }

    [Fact]
    public void ImportManagedModuleRepository_imports_profile_file_and_is_quiet_by_default()
    {
        using var profileRoot = new TemporaryDirectory();
        using var sourceRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        var sourcePath = Path.Combine(sourceRoot.Path, "repositories.json");
        new ModuleRepositoryProfileStore(sourcePath).WriteProfilesFile(sourcePath, new[]
        {
            new ModuleRepositoryProfile
            {
                Name = "Internal",
                Provider = PrivateGalleryProvider.NuGet,
                RepositoryName = "Internal",
                RepositoryUri = "https://packages.example.test/nuget/v3/index.json",
                Trusted = true
            }
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Import-ManagedModuleRepository")
            .AddParameter("Path", sourcePath);

        Assert.Empty(ps.Invoke());
        AssertNoPowerShellErrors(ps);

        var profile = Assert.Single(new ModuleRepositoryProfileStore().GetProfiles());
        Assert.Equal("Internal", profile.Name);
        Assert.True(profile.Trusted);
    }

    [Fact]
    public void UnregisterManagedModuleRepository_removes_profile_with_pass_thru()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Internal",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "Internal",
            RepositoryUri = "https://packages.example.test/nuget/v3/index.json"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Unregister-ManagedModuleRepository")
            .AddParameter("Name", "Internal")
            .AddParameter("PassThru");

        Assert.True((bool)Assert.Single(ps.Invoke()).BaseObject);
        AssertNoPowerShellErrors(ps);
        Assert.Empty(new ModuleRepositoryProfileStore().GetProfiles());
    }

    [Fact]
    public void UnregisterManagedModuleRepository_accepts_multiple_names()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        var store = new ModuleRepositoryProfileStore();
        store.SaveProfile(new ModuleRepositoryProfile
        {
            Name = "One",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "One",
            RepositoryUri = "https://packages.example.test/one/v3/index.json"
        });
        store.SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Two",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "Two",
            RepositoryUri = "https://packages.example.test/two/v3/index.json"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Unregister-ManagedModuleRepository")
            .AddParameter("Name", new[] { "One", "Two" })
            .AddParameter("PassThru");

        var results = ps.Invoke().Select(result => Assert.IsType<bool>(result.BaseObject)).ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(new[] { true, true }, results);
        Assert.Empty(new ModuleRepositoryProfileStore().GetProfiles());
    }

    [Fact]
    public void UnregisterManagedModuleRepository_accepts_pipeline_property_names()
    {
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        var store = new ModuleRepositoryProfileStore();
        store.SaveProfile(new ModuleRepositoryProfile
        {
            Name = "PipeOne",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "PipeOne",
            RepositoryUri = "https://packages.example.test/pipe-one/v3/index.json"
        });
        store.SaveProfile(new ModuleRepositoryProfile
        {
            Name = "PipeTwo",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "PipeTwo",
            RepositoryUri = "https://packages.example.test/pipe-two/v3/index.json"
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript("[pscustomobject]@{ Name = 'PipeOne' }, [pscustomobject]@{ Name = 'PipeTwo' } | Unregister-ManagedModuleRepository -PassThru");

        var results = ps.Invoke().Select(result => Assert.IsType<bool>(result.BaseObject)).ToArray();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(new[] { true, true }, results);
        Assert.Empty(new ModuleRepositoryProfileStore().GetProfiles());
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static IDisposable UseProfileStore(string root)
    {
        Directory.CreateDirectory(root);
        return new CompositeDisposable(
            new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", Path.Combine(root, "profiles.json")),
            new TestEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", Path.Combine(root, "machine-profiles.json")));
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;

        internal CompositeDisposable(params IDisposable[] items)
            => _items = items;

        public void Dispose()
        {
            foreach (var item in _items.Reverse())
                item.Dispose();
        }
    }

    private sealed class TestEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        internal TestEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
