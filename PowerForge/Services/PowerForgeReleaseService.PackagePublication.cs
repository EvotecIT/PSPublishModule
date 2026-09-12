namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>Re-enters only the publication side of the captured package lane after all staging checks pass.</summary>
    private bool PublishValidatedPackageCheckpoint(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request,
        string configPath, PowerForgeReleaseResult result)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            var publication = _executePackages(new ProjectBuildHostRequest {
                ConfigPath = configPath,
                PublicationCheckpoint = result.Packages ?? throw new InvalidOperationException("Package build checkpoint is missing."),
                PublicationAssets = result.ReleaseAssetEntries,
                RemotePublishAttempted = () => ValidatePostBuildSourceState(request),
                CancellationToken = request.CancellationToken
            }, spec.Packages!, configPath);
            request.CancellationToken.ThrowIfCancellationRequested();
            result.Packages = publication;
            if (!publication.Success)
                throw new InvalidOperationException(publication.ErrorMessage ?? "Deferred package publication failed.");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            result.Success = false;
            result.ErrorMessage = exception.Message;
            if (result.Packages is not null)
            {
                result.Packages.Success = false;
                result.Packages.ErrorMessage = exception.Message;
                result.Packages.Result.Success = false;
                result.Packages.Result.ErrorMessage = exception.Message;
            }
            return false;
        }
    }
}
