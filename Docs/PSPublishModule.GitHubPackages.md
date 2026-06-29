# PSPublishModule GitHub Packages NuGet Feed

This note describes the private GitHub Packages path for internal .NET packages
such as `Licensing.Verification`. Use this when a product repository needs a
runtime package without depending on a sibling local checkout.

## Why

GraphEssentialsX, TestimoX, and future products should restore private runtime
packages from a feed in CI. Local folders such as `C:\Support\GitHub\Licensing`
are useful for development proof, but they must not be required by product CI.

GitHub Packages is the preferred private feed for neutral internal packages:

- packages stay private unless the package owner changes visibility
- repository or team access can be granted from the package settings
- GitHub Actions can use `GITHUB_TOKEN` when the package grants that repository
  access
- cross-repository restores can use an organization secret containing a classic
  PAT with `read:packages`

## Maintainer Profile

Create a non-secret profile for the organization-scoped NuGet feed:

```powershell
Set-ManagedModuleRepository -Name LicensingGitHub -Provider GitHubPackages -GitHubOwner EvotecIT -RepositoryName github-evotec
```

The profile resolves to:

```text
https://nuget.pkg.github.com/EvotecIT/index.json
```

## Publish

Package from the owning repository, then publish through the saved profile:

```powershell
dotnet pack .\Licensing.Verification\Licensing.Verification.csproj -c Release -o .\Artifacts\Packages
Publish-NugetPackage -Path .\Artifacts\Packages -ProfileName LicensingGitHub -SkipDuplicate
```

`Publish-NugetPackage` uses the supplied `-ApiKey` when provided. For
GitHub Packages profiles, it can also use `GITHUB_TOKEN` or `GH_TOKEN`.

## Project Build Publish

For repository project builds, prefer config over repo-local authentication
helpers. `UseGitHubPackages` resolves both version lookup and package publish
to the organization feed:

```json
{
  "RootPath": "..",
  "IncludeProjects": [ "ComputerX", "ADPlayground.Monitoring" ],
  "UseGitHubPackages": true,
  "GitHubPackagesOwner": "EvotecIT",
  "GitHubAccessTokenEnvName": "GITHUB_TOKEN",
  "Build": true,
  "PublishNuget": true
}
```

When `GitHubPackagesOwner` is omitted, PowerForge uses `GitHubUsername`. The
resolved feed is `https://nuget.pkg.github.com/<owner>/index.json`. The GitHub
token is reused as the `dotnet nuget push` API key unless a dedicated
`PublishApiKey`, `PublishApiKeyFilePath`, or `PublishApiKeyEnvName` is supplied.

This is the migration path for repositories such as TestimoX that currently
carry repo-local GitHub Packages auth/test scripts. Keep the repo wrapper focused
on invoking PowerForge, move feed ownership into `project.build.json`, and pass
the token through the existing GitHub token or package-token environment
variable.

## Consumer CI Restore

Consumer repositories should add the GitHub Packages source before `dotnet
restore` and should keep the package version pinned in the project or lock file.

When the package grants the consumer repository GitHub Actions access:

```powershell
dotnet nuget add source --username EvotecIT --password $env:GITHUB_TOKEN --store-password-in-clear-text --name github-evotec "https://nuget.pkg.github.com/EvotecIT/index.json"
dotnet restore
```

When the package is not directly granted to the consumer repository, use a
private organization secret with a classic PAT that has `read:packages`:

```powershell
dotnet nuget add source --username EvotecIT --password $env:LICENSING_PACKAGES_TOKEN --store-password-in-clear-text --name github-evotec "https://nuget.pkg.github.com/EvotecIT/index.json"
dotnet restore
```

## Recommended Package Source Mapping

Route only private package ids to GitHub Packages so public dependencies still
restore from nuget.org:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-evotec" value="https://nuget.pkg.github.com/EvotecIT/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="github-evotec">
      <package pattern="Licensing.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Do not commit clear-text tokens. Prefer runtime `dotnet nuget add source` in CI
or a generated temporary `NuGet.config` in the workspace.
