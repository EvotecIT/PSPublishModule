using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs one or more modules from a private repository, optionally bootstrapping Azure Artifacts registration first.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet provides the simplified end-user flow for private gallery onboarding. You can point it at an existing
/// repository name or provide Azure Artifacts details and let the cmdlet register the repository before installing
/// the requested modules.
/// </para>
/// </remarks>
/// <example>
/// <summary>Install modules from an already registered repository</summary>
/// <code>Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'</code>
/// </example>
/// <example>
/// <summary>Register an Azure Artifacts repository and install modules in one command</summary>
/// <code>Install-PrivateModule -Name 'ModuleA', 'ModuleB' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -PromptForCredential</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "PrivateModule", DefaultParameterSetName = ParameterSetRepository, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleDependencyInstallResult))]
public sealed class InstallPrivateModuleCommand : PSCmdlet
{
    private const string ParameterSetRepository = "Repository";
    private const string ParameterSetAzureArtifacts = "AzureArtifacts";

    /// <summary>Module names to install.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Name of an already registered repository.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetRepository)]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = string.Empty;

    /// <summary>Private gallery provider. Currently only AzureArtifacts is supported.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public PrivateGalleryProvider Provider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Organization")]
    [ValidateNotNullOrEmpty]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Project")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Feed")]
    [ValidateNotNullOrEmpty]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>Optional repository name override when Azure Artifacts details are supplied.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public string? RepositoryName { get; set; }

    /// <summary>Registration strategy used when Azure Artifacts details are supplied. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public RepositoryRegistrationTool Tool { get; set; } = RepositoryRegistrationTool.Auto;

    /// <summary>Bootstrap/authentication mode used when Azure Artifacts details are supplied. Auto prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    [Alias("Mode")]
    public PrivateGalleryBootstrapMode BootstrapMode { get; set; } = PrivateGalleryBootstrapMode.Auto;

    /// <summary>When true, marks the repository as trusted during automatic registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public bool Trusted { get; set; } = true;

    /// <summary>Optional PSResourceGet repository priority used during automatic registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public int? Priority { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Prompts interactively for repository credentials.</summary>
    [Parameter]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before automatic registration.</summary>
    [Parameter(ParameterSetName = ParameterSetAzureArtifacts)]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>Includes prerelease versions when supported by the selected installer.</summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Forces reinstall even when a matching version is already present.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Executes the install workflow.</summary>
    protected override void ProcessRecord()
    {
        var modules = PrivateGalleryCommandSupport.BuildDependencies(Name);
        var repositoryName = Repository;
        RepositoryCredential? credential;
        var preferPowerShellGet = false;

        if (ParameterSetName == ParameterSetAzureArtifacts)
        {
            PrivateGalleryCommandSupport.EnsureProviderSupported(Provider);

            var endpoint = AzureArtifactsRepositoryEndpoints.Create(
                AzureDevOpsOrganization,
                AzureDevOpsProject,
                AzureArtifactsFeed,
                RepositoryName);
            var prerequisiteInstall = PrivateGalleryCommandSupport.EnsureBootstrapPrerequisites(this, InstallPrerequisites.IsPresent);
            var allowInteractivePrompt = !MyInvocation.BoundParameters.ContainsKey("WhatIf");

            repositoryName = endpoint.RepositoryName;
            var credentialResolution = PrivateGalleryCommandSupport.ResolveCredential(
                this,
                repositoryName,
                BootstrapMode,
                CredentialUserName,
                CredentialSecret,
                CredentialSecretFilePath,
                PromptForCredential,
                prerequisiteInstall.Status,
                allowInteractivePrompt);
            credential = credentialResolution.Credential;

            var registration = PrivateGalleryCommandSupport.EnsureAzureArtifactsRepositoryRegistered(
                this,
                AzureDevOpsOrganization,
                AzureDevOpsProject,
                AzureArtifactsFeed,
                RepositoryName,
                Tool,
                Trusted,
                Priority,
                BootstrapMode,
                credentialResolution.BootstrapModeUsed,
                credentialResolution.CredentialSource,
                credential,
                prerequisiteInstall.Status,
                shouldProcessAction: Tool == RepositoryRegistrationTool.Auto
                    ? "Register module repository using Auto (prefer PSResourceGet, fall back to PowerShellGet)"
                    : $"Register module repository using {Tool}");
            registration.InstalledPrerequisites = prerequisiteInstall.InstalledPrerequisites;
            registration.PrerequisiteInstallMessages = prerequisiteInstall.Messages;

            if (!registration.RegistrationPerformed)
            {
                WriteWarning($"Repository '{registration.RepositoryName}' was not registered because the operation was skipped. Module installation was not attempted.");
                return;
            }

            PrivateGalleryCommandSupport.WriteRegistrationSummary(this, registration);
            WriteVerbose($"Repository '{registration.RepositoryName}' is ready for installation.");

            if (credential is null &&
                !registration.InstallPSResourceReady &&
                !registration.InstallModuleReady)
            {
                var hint = string.IsNullOrWhiteSpace(registration.RecommendedBootstrapCommand)
                    ? string.Empty
                    : $" Recommended next step: {registration.RecommendedBootstrapCommand}";
                throw new InvalidOperationException(
                    $"Repository '{registration.RepositoryName}' was registered, but no native install path is ready for bootstrap mode {registration.BootstrapModeUsed}.{hint}");
            }

            preferPowerShellGet = credential is null &&
                                  string.Equals(registration.PreferredInstallCommand, "Install-Module", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            credential = PrivateGalleryCommandSupport.ResolveOptionalCredential(
                this,
                repositoryName,
                CredentialUserName,
                CredentialSecret,
                CredentialSecretFilePath,
                PromptForCredential);
        }

        if (!ShouldProcess($"{modules.Count} module(s) from repository '{repositoryName}'", Force.IsPresent ? "Install or reinstall private modules" : "Install private modules"))
            return;

        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var installer = new ModuleDependencyInstaller(new PowerShellRunner(), logger);
        var results = installer.EnsureInstalled(
            modules,
            force: Force.IsPresent,
            repository: repositoryName,
            credential: credential,
            prerelease: Prerelease.IsPresent,
            preferPowerShellGet: preferPowerShellGet,
            timeoutPerModule: TimeSpan.FromMinutes(10));

        WriteObject(results, enumerateCollection: true);
    }
}
