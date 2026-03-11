namespace PowerForge;

internal interface IPrivateGalleryHost
{
    bool ShouldProcess(string target, string action);
    bool IsWhatIfRequested { get; }
    RepositoryCredential? PromptForCredential(string caption, string message);
    void WriteVerbose(string message);
    void WriteWarning(string message);
}
