using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PSResourceGetPublishRepositoryTests
{
    [Theory]
    [InlineData(false, false, "V3", RepositoryApiVersion.V3)]
    [InlineData(true, false, "V3", RepositoryApiVersion.V3)]
    [InlineData(false, true, "V3", RepositoryApiVersion.V3)]
    [InlineData(true, true, "V3", RepositoryApiVersion.V3)]
    [InlineData(true, false, "V2", RepositoryApiVersion.V3)]
    [InlineData(true, false, "V3", RepositoryApiVersion.V2)]
    [InlineData(true, false, "V2", RepositoryApiVersion.Auto)]
    public void Acquire_publish_repository_preserves_existing_consumer_and_upload_settings(
        bool existingUpload, bool activePublisher, string registeredApiVersion, RepositoryApiVersion requestedApiVersion)
    {
        using var root = new TemporaryDirectory();
        var statePath = Path.Combine(root.Path, "registrations.json");
        var fixture = """
            function Import-Module { param($Name, $ErrorAction) }
            $script:repositories = @{
              Consumer = @{ Name = 'Consumer'; Uri = 'https://feed.test/virtual/index.json'; Trusted = $false; Priority = 19 }
            }
            if (__EXISTING__) {
              $script:repositories.Upload = @{ Name = 'Upload'; Uri = 'https://feed.test/local/index.json'; Trusted = $false; Priority = 23; ApiVersion = '__API__' }
            }
            if (__ACTIVE__) {
              $script:repositories['PowerForgePublish-11111111111111111111111111111111'] = @{
                Name = 'PowerForgePublish-11111111111111111111111111111111'; Uri = 'https://feed.test/local/index.json'; Trusted = $true; Priority = 1
              }
            }
            function Write-State { [IO.File]::WriteAllText('__STATE__', ($script:repositories | ConvertTo-Json -Depth 8)) }
            Write-State
            function Get-PSResourceRepository {
              param($Name, $ErrorAction)
              if ($Name) { return $script:repositories[$Name] }
              return @($script:repositories.Values)
            }
            function Set-PSResourceRepository { throw 'Existing registrations must not be changed.' }
            function Register-PSResourceRepository {
              param($Name, $Uri, $Trusted, $Priority, $ApiVersion, $Force, $ErrorAction)
              $script:repositories[$Name] = @{ Name = $Name; Uri = $Uri; Trusted = $Trusted; Priority = $Priority; ApiVersion = $ApiVersion }
              Write-State
            }
            """.Replace("__EXISTING__", existingUpload ? "$true" : "$false")
                .Replace("__ACTIVE__", activePublisher ? "$true" : "$false")
                .Replace("__API__", registeredApiVersion)
                .Replace("__STATE__", statePath.Replace("'", "''"));
        var client = new PSResourceGetClient(new RegistrationFixtureRunner(root.Path, fixture), new NullLogger());

        var acquired = client.AcquirePublishRepository("https://feed.test/local/index.json", true, 1, requestedApiVersion);
        var compatibleUpload = existingUpload && (requestedApiVersion == RepositoryApiVersion.Auto ||
            registeredApiVersion.Equals(requestedApiVersion.ToString(), StringComparison.OrdinalIgnoreCase));

        using var state = JsonDocument.Parse(File.ReadAllText(statePath));
        var consumer = state.RootElement.GetProperty("Consumer");
        Assert.Equal("https://feed.test/virtual/index.json", consumer.GetProperty("Uri").GetString());
        Assert.False(consumer.GetProperty("Trusted").GetBoolean());
        Assert.Equal(19, consumer.GetProperty("Priority").GetInt32());
        Assert.Equal(!compatibleUpload, acquired.Created);
        if (activePublisher)
        {
            Assert.NotEqual("PowerForgePublish-11111111111111111111111111111111", acquired.Name);
            Assert.Equal("https://feed.test/local/index.json", state.RootElement
                .GetProperty("PowerForgePublish-11111111111111111111111111111111").GetProperty("Uri").GetString());
        }
        if (existingUpload)
        {
            Assert.False(state.RootElement.GetProperty("Upload").GetProperty("Trusted").GetBoolean());
            Assert.Equal(23, state.RootElement.GetProperty("Upload").GetProperty("Priority").GetInt32());
            Assert.Equal(registeredApiVersion, state.RootElement.GetProperty("Upload").GetProperty("ApiVersion").GetString());
        }
        if (compatibleUpload)
            Assert.Equal("Upload", acquired.Name);
        else
        {
            Assert.StartsWith("PowerForgePublish-", acquired.Name);
            Assert.Equal("https://feed.test/local/index.json", state.RootElement.GetProperty(acquired.Name).GetProperty("Uri").GetString());
            Assert.Equal(requestedApiVersion.ToString(), state.RootElement.GetProperty(acquired.Name).GetProperty("ApiVersion").GetString());
        }
    }

    private sealed class RegistrationFixtureRunner(string root, string fixture) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var script = File.ReadAllText(request.ScriptPath!);
            var scriptPath = Path.Combine(root, "ensure.ps1");
            File.WriteAllText(scriptPath, script.Replace("$ErrorActionPreference = 'Stop'", "$ErrorActionPreference = 'Stop'\n" + fixture));
            return new PowerShellRunner().Run(new PowerShellRunRequest(scriptPath, request.Arguments,
                TimeSpan.FromSeconds(30), workingDirectory: root));
        }
    }
}
