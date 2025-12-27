using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds a .NET project in Release configuration and prepares release artefacts.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DotNetReleaseBuild", SupportsShouldProcess = true)]
[OutputType(typeof(DotNetReleaseBuildResult))]
public sealed class InvokeDotNetReleaseBuildCommand : PSCmdlet
{
    /// <summary>Path to the folder containing the project (*.csproj) file (or the csproj file itself).</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

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
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new DotNetReleaseBuildService(logger);

        string fullProjectPath;
        try { fullProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath); }
        catch { fullProjectPath = ProjectPath; }

        var mappedStore = LocalStore == CertificateStoreLocation.LocalMachine
            ? PowerForge.CertificateStoreLocation.LocalMachine
            : PowerForge.CertificateStoreLocation.CurrentUser;

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
            return;
        }

        var target = !string.IsNullOrWhiteSpace(plan.ProjectName) && !string.IsNullOrWhiteSpace(plan.Version)
            ? $"{plan.ProjectName} v{plan.Version}"
            : spec.ProjectPath;

        if (!ShouldProcess(target, "Build and pack .NET project"))
        {
            if (PackDependencies.IsPresent && plan.DependencyProjects.Length > 0)
            {
                WriteVerbose($"Would also pack {plan.DependencyProjects.Length} dependency projects: {string.Join(", ", plan.DependencyProjects.Select(Path.GetFileName))}");
            }

            WriteObject(plan);
            return;
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

    private void InvokeRegisterCertificate(
        string releasePath,
        CertificateStoreLocation store,
        string thumbprint,
        string timeStampServer,
        string[] includePatterns)
    {
        var module = MyInvocation.MyCommand?.Module;
        if (module is null) return;

        var sb = ScriptBlock.Create(@"
param($path,$store,$thumb,$ts,$include)
Register-Certificate -Path $path -LocalStore $store -Thumbprint $thumb -TimeStampServer $ts -Include $include
");
        var bound = module.NewBoundScriptBlock(sb);
        bound.Invoke(releasePath, store.ToString(), thumbprint, timeStampServer, includePatterns);
    }

    private sealed class CmdletLogger : ILogger
    {
        private readonly PSCmdlet _cmdlet;
        public bool IsVerbose { get; set; }

        public CmdletLogger(PSCmdlet cmdlet, bool isVerbose)
        {
            _cmdlet = cmdlet;
            IsVerbose = isVerbose;
        }

        public void Info(string message) => _cmdlet.WriteVerbose(message);
        public void Success(string message) => _cmdlet.WriteVerbose(message);
        public void Warn(string message) => _cmdlet.WriteWarning(message);
        public void Error(string message) => _cmdlet.WriteVerbose(message);
        public void Verbose(string message)
        {
            if (!IsVerbose) return;
            _cmdlet.WriteVerbose(message);
        }
    }
}

