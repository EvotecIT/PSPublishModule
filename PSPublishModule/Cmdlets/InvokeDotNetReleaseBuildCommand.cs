using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds a .NET project in Release configuration and prepares release artefacts.
/// </summary>
/// <remarks>
/// <para>
/// The cmdlet discovers the <c>.csproj</c> file (when a directory is provided), reads <c>VersionPrefix</c> from the
/// project, then runs <c>dotnet build</c> and <c>dotnet pack</c> in Release (by default). It produces a ZIP snapshot of
/// the release output and returns a typed result object for each input project path.
/// </para>
/// <para>
/// Use <c>-WhatIf</c> to preview the planned outputs without running build/pack/sign operations.
/// </para>
/// </remarks>
/// <example>
/// <summary>Build and pack a project (and its dependency projects)</summary>
/// <code>Invoke-DotNetReleaseBuild -ProjectPath '.\MyLibrary\MyLibrary.csproj' -PackDependencies</code>
/// </example>
/// <example>
/// <summary>Build and sign a project (certificate thumbprint)</summary>
/// <code>Invoke-DotNetReleaseBuild -ProjectPath '.\MyLibrary\MyLibrary.csproj' -CertificateThumbprint '0123456789ABCDEF' -LocalStore CurrentUser</code>
/// </example>
/// <example>
/// <summary>Preview the release build plan (no build)</summary>
/// <code>Invoke-DotNetReleaseBuild -ProjectPath '.\MyLibrary' -PackDependencies -WhatIf</code>
/// </example>
/// <example>
/// <summary>Build multiple projects in one invocation</summary>
/// <code>Invoke-DotNetReleaseBuild -ProjectPath '.\ProjectA\ProjectA.csproj', '.\ProjectB\ProjectB.csproj' -PackDependencies</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "DotNetReleaseBuild", SupportsShouldProcess = true)]
[OutputType(typeof(DotNetReleaseBuildResult))]
public sealed class InvokeDotNetReleaseBuildCommand : PSCmdlet
{
    /// <summary>Path to the folder containing the project (*.csproj) file (or the csproj file itself).</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] ProjectPath { get; set; } = Array.Empty<string>();

    /// <summary>Optional certificate thumbprint used to sign assemblies and packages. When omitted, no signing is performed.</summary>
    [Parameter]
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location used when searching for the signing certificate. Default: CurrentUser.</summary>
    [Parameter]
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Timestamp server URL used while signing. Default: http://timestamp.digicert.com.</summary>
    [Parameter]
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>When enabled, also packs all project dependencies that have their own .csproj files.</summary>
    [Parameter]
    public SwitchParameter PackDependencies { get; set; }

    /// <summary>Executes build/pack/sign operations and returns a result object.</summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var service = new DotNetReleaseBuildService(logger);

        var mappedStore = LocalStore == CertificateStoreLocation.LocalMachine
            ? PowerForge.CertificateStoreLocation.LocalMachine
            : PowerForge.CertificateStoreLocation.CurrentUser;

        foreach (var project in ProjectPath ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(project))
            {
                WriteObject(new DotNetReleaseBuildResult
                {
                    Success = false,
                    ErrorMessage = "ProjectPath contains an empty value."
                });
                continue;
            }

            string fullProjectPath;
            try { fullProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(project); }
            catch { fullProjectPath = project; }

            var spec = new DotNetReleaseBuildSpec
            {
                ProjectPath = fullProjectPath,
                Configuration = "Release",
                CertificateThumbprint = CertificateThumbprint,
                LocalStore = mappedStore,
                TimeStampServer = TimeStampServer,
                PackDependencies = PackDependencies.IsPresent
            };

            // Plan first (used for ShouldProcess messaging and -WhatIf output).
            spec.WhatIf = true;
            var plan = service.Execute(spec);
            spec.WhatIf = false;

            if (!string.IsNullOrWhiteSpace(plan.ErrorMessage))
            {
                WriteObject(plan);
                continue;
            }

            var target = !string.IsNullOrWhiteSpace(plan.ProjectName) && !string.IsNullOrWhiteSpace(plan.Version)
                ? $"{plan.ProjectName} v{plan.Version}"
                : spec.ProjectPath;

            if (!ShouldProcess(target, "Build and pack .NET project"))
            {
                if (PackDependencies.IsPresent && plan.DependencyProjects.Length > 0)
                {
                    WriteVerbose($"Would also pack {plan.DependencyProjects.Length} dependency projects: {string.Join(", ", plan.DependencyProjects.Select(System.IO.Path.GetFileName))}");
                }

                WriteObject(plan);
                continue;
            }

            Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies = null;
            if (!string.IsNullOrWhiteSpace(CertificateThumbprint))
            {
                signAssemblies = req => InvokeRegisterCertificate(
                    req.ReleasePath,
                    LocalStore,
                    req.CertificateThumbprint,
                    req.TimeStampServer,
                    req.IncludePatterns);
            }

            var result = service.Execute(spec, signAssemblies);
            WriteObject(result);
        }
    }

    private void InvokeRegisterCertificate(
        string releasePath,
        CertificateStoreLocation store,
        string thumbprint,
        string timeStampServer,
        string[] includePatterns)
    {
        var sb = ScriptBlock.Create(@"
param($path,$store,$thumb,$ts,$include)
Register-Certificate -Path $path -LocalStore $store -Thumbprint $thumb -TimeStampServer $ts -Include $include
");

        // ModuleInfo.NewBoundScriptBlock works only for script modules. PSPublishModule cmdlets execute
        // in the binary module context, so we must invoke directly.
        sb.Invoke(releasePath, store.ToString(), thumbprint, timeStampServer, includePatterns);
    }
}
