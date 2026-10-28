namespace PowerForge;

public sealed partial class PSResourceGetClient
{
    private static string ComposePublicGalleryFailureDiagnostics(string script)
        => script.Replace("# POWERFORGE_PUBLIC_GALLERY_FAILURE_DIAGNOSTICS",
            EmbeddedScripts.Load("Scripts/RepositoryDiagnostics/Collect-PublicGalleryFailure.ps1"));
}
