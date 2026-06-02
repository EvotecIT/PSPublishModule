using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports a known PowerShell module through a curated AssemblyLoadContext isolation profile.
/// </summary>
/// <remarks>
/// <para>
/// Import-IsolatedModule is intended for PowerShell 7+ sessions where one service module's binary
/// dependencies cannot safely share the default AssemblyLoadContext with another module. The command
/// resolves the selected profile, copies the target module to a temporary working folder, generates a
/// profile-specific wrapper, loads selected binary assemblies through a module-scoped AssemblyLoadContext,
/// and imports the generated wrapper into the current runspace.
/// </para>
/// <para>
/// Built-in profiles are maintained by PowerForge and are deliberately curated. Use the
/// ExchangeOnlineManagement profile when Exchange Online needs to coexist with Az.Storage or another
/// module that has already loaded incompatible Microsoft.OData assemblies. Use the MicrosoftTeams
/// profile when Teams should keep its MSAL, IdentityModel, policy administration, and ConfigAPI binary
/// stack out of the default context.
/// </para>
/// <para>
/// This command does not authenticate to the isolated module. After importing the profile, call the
/// target module's normal connection command, such as Connect-ExchangeOnline or Connect-MicrosoftTeams.
/// Windows PowerShell 5.1 is not supported because AssemblyLoadContext is only available on CoreCLR.
/// </para>
/// </remarks>
/// <example>
/// <summary>Start an isolated Exchange Online session</summary>
/// <code>Import-IsolatedModule -Profile ExchangeOnlineManagement&#xA;$commands = 'Get-EXOMailbox', 'Get-ConnectionInformation'&#xA;Connect-ExchangeOnline -ShowBanner:$false -CommandName $commands&#xA;Get-ConnectionInformation | Format-Table UserPrincipalName, ConnectionUri&#xA;Get-EXOMailbox -ResultSize 5 | Select-Object DisplayName, PrimarySmtpAddress&#xA;Disconnect-ExchangeOnline -Confirm:$false</code>
/// <para>
/// Imports ExchangeOnlineManagement through the curated profile, connects with the normal EXO
/// cmdlets, runs a small mailbox query, and disconnects cleanly.
/// </para>
/// </example>
/// <example>
/// <summary>Use Exchange Online after Az.Storage has loaded OData 7.6</summary>
/// <code>Import-Module Az.Storage&#xA;$defaultODataBefore = [System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies |&#xA;    Where-Object { $_.GetName().Name -like 'Microsoft.OData*' }&#xA;Import-IsolatedModule -Profile ExchangeOnlineManagement&#xA;Connect-ExchangeOnline -ShowBanner:$false&#xA;$defaultODataBefore | ForEach-Object { $_.GetName().Name + ' ' + $_.GetName().Version }&#xA;Get-EXOMailbox -ResultSize 1 | Select-Object DisplayName, PrimarySmtpAddress&#xA;Disconnect-ExchangeOnline -Confirm:$false</code>
/// <para>
/// Keeps Az.Storage's Microsoft.OData 7.6 assemblies in the default context while Exchange Online loads
/// Microsoft.OData 7.22 assemblies in ExchangeOnlineManagement.ALC.
/// </para>
/// </example>
/// <example>
/// <summary>Start an isolated Microsoft Teams session</summary>
/// <code>Import-IsolatedModule -Profile MicrosoftTeams&#xA;Connect-MicrosoftTeams -UseDeviceAuthentication&#xA;$teams = Get-Team&#xA;$teams | Select-Object -First 10 DisplayName, GroupId, Visibility&#xA;Disconnect-MicrosoftTeams</code>
/// <para>
/// Imports Teams cmdlets from MicrosoftTeams.ALC and then uses the normal Teams connection workflow.
/// </para>
/// </example>
/// <example>
/// <summary>Inspect the generated isolated Teams module</summary>
/// <code>$result = Import-IsolatedModule -Profile MicrosoftTeams -PassThru&#xA;$result | Format-List ProfileName, ContextName, IsolatedImportPath, IsolatedScriptPath, WorkPath&#xA;Get-Command -Module MicrosoftTeams.ALC | Measure-Object&#xA;[System.Runtime.Loader.AssemblyLoadContext]::All |&#xA;    Where-Object Name -eq $result.ContextName |&#xA;    ForEach-Object {&#xA;        $_.Assemblies |&#xA;            Where-Object { $_.GetName().Name -like 'Microsoft.Teams*' } |&#xA;            Select-Object -ExpandProperty FullName&#xA;    }</code>
/// <para>
/// Returns the generated wrapper details and confirms that Teams assemblies were loaded in the
/// profile-specific AssemblyLoadContext.
/// </para>
/// </example>
/// <example>
/// <summary>Import a specific installed module copy by path</summary>
/// <code>$module = Get-Module -ListAvailable ExchangeOnlineManagement |&#xA;    Sort-Object Version -Descending |&#xA;    Select-Object -First 1&#xA;$result = Import-IsolatedModule -Profile ExchangeOnlineManagement -Path $module.ModuleBase -PassThru&#xA;$result | Select-Object ProfileName, ModuleName, SourceModuleBase, IsolatedImportPath&#xA;Get-Module ExchangeOnlineManagement | Select-Object Name, Version, ModuleBase</code>
/// <para>
/// Uses the profile rules but bypasses PSModulePath discovery by pointing at a specific module base
/// folder. The profile still validates its minimum supported module version.
/// </para>
/// </example>
/// <example>
/// <summary>Use a deterministic work root for diagnostics</summary>
/// <code>$workRoot = Join-Path $env:TEMP 'PowerForge-Isolated'&#xA;$result = Import-IsolatedModule -Profile MicrosoftTeams -WorkRoot $workRoot -PassThru&#xA;Get-ChildItem -LiteralPath $result.WorkPath -Recurse | Select-Object -First 20 FullName&#xA;Get-Content -LiteralPath $result.IsolatedScriptPath -TotalCount 40</code>
/// <para>
/// Creates the generated module copy under the supplied root and inspects the generated wrapper.
/// </para>
/// </example>
/// <example>
/// <summary>Preview an import without creating the isolated module copy</summary>
/// <code>Import-IsolatedModule -Profile ExchangeOnlineManagement -WhatIf</code>
/// <para>
/// Uses ShouldProcess support to show the intended operation without preparing or importing the wrapper.
/// </para>
/// </example>
[Cmdlet(VerbsData.Import, "IsolatedModule", SupportsShouldProcess = true)]
[OutputType(typeof(IsolatedModuleImportResult))]
public sealed class ImportIsolatedModuleCommand : PSCmdlet
{
    /// <summary>Name of the built-in isolation profile to use.</summary>
    /// <remarks>
    /// <para>
    /// Supported built-in profiles are ExchangeOnlineManagement and MicrosoftTeams. Profile names are
    /// resolved case-insensitively.
    /// </para>
    /// </remarks>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Profile { get; set; } = string.Empty;

    /// <summary>Optional module name override. When omitted, the profile's module name is used.</summary>
    /// <remarks>
    /// <para>
    /// Use this only when testing a compatible module installed under a non-standard module name. The
    /// selected profile still controls script patching, binary imports, and the load-context name.
    /// </para>
    /// </remarks>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Optional module base path or manifest path. When omitted, the profile module is resolved from PSModulePath.</summary>
    /// <remarks>
    /// <para>
    /// Use Path to test a specific installed version or a copied module payload. Directory paths are treated
    /// as module bases. File paths are resolved to their parent directory.
    /// </para>
    /// </remarks>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Optional root folder for generated isolated module copies.</summary>
    /// <remarks>
    /// <para>
    /// When omitted, generated module copies are created under the system temp folder in
    /// PowerForge\IsolatedModules\&lt;profile&gt;. Use WorkRoot when you want to inspect generated wrappers
    /// or keep diagnostic artifacts in a known location.
    /// </para>
    /// </remarks>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? WorkRoot { get; set; }

    /// <summary>Return the generated import result.</summary>
    /// <remarks>
    /// <para>
    /// The result includes the source module path, generated script and manifest paths, import path,
    /// work root, load-context name, and profile counts.
    /// </para>
    /// </remarks>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Imports the isolated module profile into the current session.</summary>
    protected override void ProcessRecord()
    {
        if (!ShouldProcess(Profile, "Import isolated module profile"))
            return;

        try
        {
            var request = new IsolatedModuleImportRequest
            {
                ProfileName = Profile,
                ModuleName = Name,
                Path = ResolveOptionalPath(Path),
                WorkRoot = ResolveOptionalPath(WorkRoot)
            };

            var result = new IsolatedModuleImportService().Import(request);
            if (PassThru)
                WriteObject(result);
        }
        catch (Exception ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "ImportIsolatedModuleFailed", ErrorCategory.InvalidOperation, Profile));
        }
    }

    private string? ResolveOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
}
