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

    public bool ShouldProcess(string target, string action)
        => _cmdlet is AsyncPSCmdlet asyncCmdlet
            ? asyncCmdlet.ShouldProcess(target, action)
            : _cmdlet.ShouldProcess(target, action);

    public bool IsWhatIfRequested =>
        _cmdlet.MyInvocation.BoundParameters.TryGetValue("WhatIf", out var whatIfValue) &&
        whatIfValue is SwitchParameter switchParameter &&
        switchParameter.IsPresent;

    public RepositoryCredential? PromptForCredential(string caption, string message)
    {
        var promptCredential = _cmdlet is AsyncPSCmdlet asyncCmdlet
            ? asyncCmdlet.PromptForCredential(caption, message, string.Empty, string.Empty)
            : _cmdlet.Host.UI.PromptForCredential(caption, message, string.Empty, string.Empty);
        if (promptCredential is null)
            return null;

        return new RepositoryCredential
        {
            UserName = promptCredential.UserName,
            Secret = promptCredential.GetNetworkCredential().Password
        };
    }

    public void WriteVerbose(string message)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet)
            asyncCmdlet.WriteVerbose(message);
        else
            _cmdlet.WriteVerbose(message);
    }

    public void WriteWarning(string message)
    {
        if (_cmdlet is AsyncPSCmdlet asyncCmdlet)
            asyncCmdlet.WriteWarning(message);
        else
            _cmdlet.WriteWarning(message);
    }
}
