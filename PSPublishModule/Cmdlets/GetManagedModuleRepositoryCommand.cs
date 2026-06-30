using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets, tests, or exports saved managed module repository profiles.
/// </summary>
/// <remarks>
/// <para>
/// Repository profiles contain non-secret feed settings. Use this cmdlet to review repository shape, test local
/// onboarding readiness, or export profile definitions for another machine without creating another command family.
/// </para>
/// </remarks>
/// <example>
/// <summary>List all saved managed module repositories</summary>
/// <code>Get-ManagedModuleRepository</code>
/// <para>Returns all repository profiles visible to the current user.</para>
/// </example>
/// <example>
/// <summary>Inspect one saved repository</summary>
/// <code>Get-ManagedModuleRepository -Name Company</code>
/// <para>Returns the saved Azure Artifacts profile named <c>Company</c>.</para>
/// </example>
/// <example>
/// <summary>Test repository readiness</summary>
/// <code>Get-ManagedModuleRepository -Name Company -Test</code>
/// <para>Returns local prerequisite and bootstrap readiness for the saved repository profile.</para>
/// </example>
/// <example>
/// <summary>Export profiles for another machine</summary>
/// <code>Get-ManagedModuleRepository -Name Company -ExportPath .\Company.repository.json -Force</code>
/// <para>Writes a non-secret JSON profile file that can be imported with <c>Initialize-ManagedModuleRepository -Path</c>.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ManagedModuleRepository", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleRepositoryProfileResult))]
[OutputType(typeof(ModuleRepositoryProfileReadinessResult))]
public sealed class GetManagedModuleRepositoryCommand : PSCmdlet
{
    /// <summary>Optional repository profile names. When omitted, all visible profiles are returned.</summary>
    [Parameter(Position = 0)]
    [Alias("ProfileName")]
    public string[]? Name { get; set; }

    /// <summary>Return readiness information instead of profile definitions.</summary>
    [Parameter]
    public SwitchParameter Test { get; set; }

    /// <summary>Optional destination JSON file for exporting the selected profiles.</summary>
    [Parameter]
    [Alias("Path")]
    public string? ExportPath { get; set; }

    /// <summary>Overwrite an existing export file.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Return profile objects after exporting.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to read. The default reads user profiles first, then machine-wide profiles.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.All;

    /// <summary>Gets, tests, or exports saved repository profiles.</summary>
    protected override void ProcessRecord()
    {
        if (Test.IsPresent)
        {
            WriteObject(GetReadinessResults(), enumerateCollection: true);
            return;
        }

        var selected = ResolveProfiles();

        if (!string.IsNullOrWhiteSpace(ExportPath))
        {
            ExportProfiles(selected);
            return;
        }

        var results = selected
            .Select(resolved => ModuleRepositoryProfileResultMapper.ToCmdletResult(
                resolved.Profile,
                resolved.Store.Path,
                resolved.Store.Scope))
            .ToArray();
        WriteObject(results, enumerateCollection: true);
    }

    private ModuleRepositoryProfileCommandSupport.ResolvedModuleRepositoryProfile[] ResolveProfiles()
    {
        if (Name is null || Name.Length == 0)
            return ModuleRepositoryProfileCommandSupport.GetUniqueProfilesWithStores(Scope);

        return Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(name => ModuleRepositoryProfileCommandSupport.ResolveRequiredWithStore(name, Scope))
            .ToArray();
    }

    private ModuleRepositoryProfileReadinessResult[] GetReadinessResults()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);
        var status = service.GetBootstrapPrerequisiteStatus();
        if (Name is null || Name.Length == 0)
        {
            return ModuleRepositoryProfileCommandSupport.GetUniqueProfilesWithStores(Scope)
                .Select(resolved => ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                    resolved.Profile,
                    resolved.Store.Path,
                    status,
                    resolved.Store.Scope))
                .ToArray();
        }

        return Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(name =>
            {
                var resolved = ModuleRepositoryProfileCommandSupport.ResolveWithStore(name, Scope);
                if (resolved.HasValue)
                {
                    return ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                        resolved.Value.Profile,
                        resolved.Value.Store.Path,
                        status,
                        resolved.Value.Store.Scope);
                }

                var missingScope = Scope == ModuleRepositoryProfileScope.All
                    ? ModuleRepositoryProfileScope.User
                    : Scope;
                var store = new ModuleRepositoryProfileStore(missingScope);
                return ModuleRepositoryProfileReadinessMapper.ToMissingProfileResult(name, store.Path, store.Scope);
            })
            .ToArray();
    }

    private void ExportProfiles(ModuleRepositoryProfileCommandSupport.ResolvedModuleRepositoryProfile[] selected)
    {
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ExportPath!);
        if (File.Exists(resolvedPath) && !Force)
            throw new IOException($"File '{resolvedPath}' already exists. Use -Force to overwrite it.");

        var profiles = selected
            .Select(static resolved => resolved.Profile)
            .ToArray();

        if (!ShouldProcess(resolvedPath, $"Export {profiles.Length} managed module repository profile(s)"))
            return;

        new ModuleRepositoryProfileStore(ModuleRepositoryProfileScope.User).WriteProfilesFile(resolvedPath, profiles);
        if (!PassThru)
            return;

        var results = selected
            .Select(resolved => ModuleRepositoryProfileResultMapper.ToCmdletResult(
                resolved.Profile,
                resolved.Store.Path,
                resolved.Store.Scope))
            .ToArray();
        WriteObject(results, enumerateCollection: true);
    }
}
