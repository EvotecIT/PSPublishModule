namespace PowerForge;

/// <summary>
/// Publish destination type for publish configuration segments.
/// </summary>
public enum PublishDestination
{
    /// <summary>Publish to a PowerShell repository (PSGallery or a named repository).</summary>
    PowerShellGallery,
    /// <summary>Publish artefacts to GitHub Releases.</summary>
    GitHub
}

/// <summary>
/// Publishing tool/provider used when publishing to a PowerShell repository.
/// </summary>
public enum PublishTool
{
    /// <summary>
    /// Choose the best available tool at runtime (prefer the managed C# publisher and use compatibility tools only when required).
    /// </summary>
    Auto,
    /// <summary>Use Microsoft.PowerShell.PSResourceGet.</summary>
    PSResourceGet,
    /// <summary>Use PowerShellGet (Publish-Module/Register-PSRepository).</summary>
    PowerShellGet,
    /// <summary>Use the managed C# module publish engine.</summary>
    ManagedModule
}

/// <summary>
/// Tool selection used when registering repositories for end users.
/// </summary>
public enum RepositoryRegistrationTool
{
    /// <summary>
    /// Prefer PSResourceGet first, then fall back to PowerShellGet when one of the tools is unavailable.
    /// </summary>
    Auto,
    /// <summary>Register the PSResourceGet repository only.</summary>
    PSResourceGet,
    /// <summary>Register the PowerShellGet repository only.</summary>
    PowerShellGet,
    /// <summary>Register both PowerShellGet and PSResourceGet repositories.</summary>
    Both
}

/// <summary>
/// Private gallery provider used by end-user repository bootstrap commands.
/// </summary>
public enum PrivateGalleryProvider
{
    /// <summary>Azure Artifacts / Azure DevOps private feed.</summary>
    AzureArtifacts = 0,
    /// <summary>Alias for Azure Artifacts / Azure DevOps private feed.</summary>
    Azure = 0,
    /// <summary>JFrog Artifactory NuGet/PowerShell repository.</summary>
    JFrog = 1,
    /// <summary>Generic NuGet-backed PowerShell repository.</summary>
    NuGet = 2,
    /// <summary>GitHub Packages NuGet registry scoped to a GitHub user or organization.</summary>
    GitHubPackages = 3,
    /// <summary>Alias for GitHub Packages NuGet registry.</summary>
    GitHub = 3
}

/// <summary>
/// Bootstrap/authentication mode used by private gallery onboarding commands.
/// </summary>
public enum PrivateGalleryBootstrapMode
{
    /// <summary>
    /// Choose the best available path: use explicit/prompted credentials when requested, otherwise prefer ExistingSession when Azure Artifacts prerequisites are ready and fall back to CredentialPrompt when they are not.
    /// </summary>
    Auto,
    /// <summary>
    /// Rely on an existing session or credential-provider flow without collecting credentials in the cmdlet.
    /// </summary>
    ExistingSession,
    /// <summary>
    /// Use credentials supplied to the cmdlet or prompt interactively for them.
    /// </summary>
    CredentialPrompt,
    /// <summary>
    /// Use JFrog CLI browser login before probing a JFrog private gallery. This validates whether the local JFrog CLI session can bridge to NuGet/PSResourceGet.
    /// </summary>
    JFrogCli
}

/// <summary>
/// Source of the credential used by a private gallery bootstrap command.
/// </summary>
public enum PrivateGalleryCredentialSource
{
    /// <summary>No credential was supplied to the cmdlet.</summary>
    None,
    /// <summary>A credential or token was supplied directly to the cmdlet.</summary>
    Supplied,
    /// <summary>A credential was collected by prompting the user.</summary>
    Prompt,
    /// <summary>An external JFrog CLI browser-login session was used.</summary>
    JFrogCli
}

/// <summary>
/// Runtime credential provider used when repository credentials must be resolved at publish time.
/// </summary>
public enum RepositoryCredentialProviderKind
{
    /// <summary>No runtime credential provider is configured.</summary>
    None,
    /// <summary>Exchange a CI-issued OIDC token for a JFrog access token with JFrog CLI.</summary>
    JFrogOidc
}

/// <summary>
/// JFrog OIDC provider implementation passed to <c>jf exchange-oidc-token</c>.
/// </summary>
public enum JFrogOidcProviderType
{
    /// <summary>GitHub Actions OIDC provider.</summary>
    GitHub,
    /// <summary>Azure DevOps / Azure OIDC provider.</summary>
    Azure,
    /// <summary>Generic OIDC-compatible provider.</summary>
    GenericOidc
}

/// <summary>
/// Profile store scope used by private gallery profile commands.
/// </summary>
public enum ModuleRepositoryProfileScope
{
    /// <summary>Use the current user's profile store.</summary>
    User,
    /// <summary>Use the machine-wide profile store shared by all users on the workstation.</summary>
    Machine,
    /// <summary>Read from user and machine-wide stores, preferring the user store when both define the same profile.</summary>
    All
}

/// <summary>
/// Tool/provider used when downloading PowerShell modules (Save-PSResource/Save-Module).
/// </summary>
public enum ModuleSaveTool
{
    /// <summary>
    /// Choose the best available tool at runtime (prefer PSResourceGet, fall back to PowerShellGet).
    /// </summary>
    Auto,
    /// <summary>Use Microsoft.PowerShell.PSResourceGet (Save-PSResource).</summary>
    PSResourceGet,
    /// <summary>Use PowerShellGet (Save-Module).</summary>
    PowerShellGet
}

/// <summary>
/// Source used when resolving required modules for artefacts (local copy vs download).
/// </summary>
public enum RequiredModulesSource
{
    /// <summary>
    /// Prefer locally available modules (Get-Module -ListAvailable) and download only when missing.
    /// </summary>
    Auto,
    /// <summary>
    /// Only copy locally available modules (do not download).
    /// </summary>
    Installed,
    /// <summary>
    /// Always download required modules (ignore locally available copies).
    /// </summary>
    Download
}

/// <summary>
/// API version for PSResourceGet repository endpoints.
/// </summary>
public enum RepositoryApiVersion
{
    /// <summary>Automatic/default behavior (omit version unless required).</summary>
    Auto = 0,
    /// <summary>NuGet v2 API.</summary>
    V2 = 1,
    /// <summary>NuGet v3 API.</summary>
    V3 = 2,
    /// <summary>OCI/container registry API (Azure Container Registry, Microsoft Artifact Registry).</summary>
    ContainerRegistry = 3,
    /// <summary>Local filesystem repository API.</summary>
    Local = 4,
    /// <summary>NuGet.Server repository API.</summary>
    NugetServer = 5
}

/// <summary>
/// Dependency kind used by module dependency configuration segments.
/// </summary>
public enum ModuleDependencyKind
{
    /// <summary>Required module dependency (manifest RequiredModules).</summary>
    RequiredModule = 0,
    /// <summary>External module dependency (PSData.ExternalModuleDependencies).</summary>
    ExternalModule = 1,
    /// <summary>Approved module dependency (selectively copied during merge).</summary>
    ApprovedModule = 2,
    /// <summary>Embedded module dependency (bundled under Internals/Modules, not written to manifest RequiredModules).</summary>
    EmbeddedModule = 3
}

/// <summary>
/// Source used when resolving Auto/Latest module dependency versions.
/// </summary>
public enum ModuleDependencyVersionSource
{
    /// <summary>Use the build default: installed metadata, with online lookup only when enabled or needed.</summary>
    Auto,
    /// <summary>Resolve from locally installed module metadata.</summary>
    Installed,
    /// <summary>Resolve from the PowerShell Gallery repository.</summary>
    PSGallery,
    /// <summary>Resolve from a publish configuration marked as the dependency version source.</summary>
    PublishRepository
}

/// <summary>
/// Encoding values used by file consistency configuration.
/// </summary>
public enum FileConsistencyEncoding
{
    /// <summary>ASCII.</summary>
    ASCII,
    /// <summary>UTF-8 (no BOM).</summary>
    UTF8,
    /// <summary>UTF-8 with BOM.</summary>
    UTF8BOM,
    /// <summary>UTF-16 (Little Endian).</summary>
    Unicode,
    /// <summary>UTF-16 (Big Endian).</summary>
    BigEndianUnicode,
    /// <summary>UTF-7.</summary>
    UTF7,
    /// <summary>UTF-32.</summary>
    UTF32
}

/// <summary>
/// Line ending values used by file consistency configuration.
/// </summary>
public enum FileConsistencyLineEnding
{
    /// <summary>CRLF (Windows).</summary>
    CRLF,
    /// <summary>LF (Unix).</summary>
    LF
}

/// <summary>
/// Scope for file consistency checks (staging/project).
/// </summary>
public enum FileConsistencyScope
{
    /// <summary>Check staging output only.</summary>
    StagingOnly,
    /// <summary>Check project root only.</summary>
    ProjectOnly,
    /// <summary>Check both staging output and project root.</summary>
    StagingAndProject
}

/// <summary>
/// Destination locations for delivery bundle items (README/CHANGELOG/LICENSE).
/// </summary>
public enum DeliveryBundleDestination
{
    /// <summary>Place files under Internals.</summary>
    Internals,
    /// <summary>Place files in the module root.</summary>
    Root,
    /// <summary>Place files in both Internals and Root.</summary>
    Both,
    /// <summary>Do not place the files.</summary>
    None
}

/// <summary>
/// Artefact type for artefact configuration.
/// </summary>
public enum ArtefactType
{
    /// <summary>Unpacked module artefact.</summary>
    Unpacked,
    /// <summary>Packed module artefact (zip).</summary>
    Packed,
    /// <summary>Script artefact (PS1 without PSD1).</summary>
    Script,
    /// <summary>Packed script artefact (zip containing PS1 without PSD1).</summary>
    ScriptPacked
}

/// <summary>
/// When to execute tests within the legacy build workflow.
/// </summary>
public enum TestExecutionWhen
{
    /// <summary>Execute tests after merge/build has produced the final module output.</summary>
    AfterMerge
}

/// <summary>
/// Apple platform targeted by an app release configuration.
/// </summary>
public enum ApplePlatform
{
    /// <summary>iOS app.</summary>
    iOS,
    /// <summary>iPadOS app.</summary>
    iPadOS,
    /// <summary>macOS app.</summary>
    macOS,
    /// <summary>tvOS app.</summary>
    tvOS,
    /// <summary>watchOS app.</summary>
    watchOS,
    /// <summary>visionOS app.</summary>
    visionOS
}

/// <summary>
/// Build number policy for Apple app local project preparation.
/// </summary>
public enum AppleBuildNumberPolicy
{
    /// <summary>Use the explicitly provided build number.</summary>
    Explicit,
    /// <summary>Leave CURRENT_PROJECT_VERSION unchanged.</summary>
    KeepExisting,
    /// <summary>Read CURRENT_PROJECT_VERSION and increment it by one.</summary>
    IncrementExisting
}
