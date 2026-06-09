using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class CmdletPrivateGalleryHost : IPrivateGalleryHost
{
    private readonly PSCmdlet _cmdlet;

    public CmdletPrivateGalleryHost(PSCmdlet cmdlet)
    {
        _cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));
    }

    public bool ShouldProcess(string target, string action) => _cmdlet.ShouldProcess(target, action);

    public bool IsWhatIfRequested =>
        _cmdlet.MyInvocation.BoundParameters.TryGetValue("WhatIf", out var whatIfValue) &&
        whatIfValue is SwitchParameter switchParameter &&
        switchParameter.IsPresent;

    public RepositoryCredential? PromptForCredential(string caption, string message)
    {
        var promptCredential = _cmdlet.Host.UI.PromptForCredential(caption, message, string.Empty, string.Empty);
        if (promptCredential is null)
            return null;

        return new RepositoryCredential
        {
            UserName = promptCredential.UserName,
            Secret = promptCredential.GetNetworkCredential().Password
        };
    }

    public void WriteVerbose(string message) => _cmdlet.WriteVerbose(message);

    public void WriteWarning(string message) => _cmdlet.WriteWarning(message);
}
