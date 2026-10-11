namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private void ValidateAppleUploadIntegrity(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result,
        AppleArchiveUploadSnapshot? uploadSnapshot,
        AppleReleaseSourceMutationMonitor? uploadMutationMonitor,
        string? approvedSha256)
    {
        try
        {
            uploadSnapshot?.WaitForExpectedXcodeExportScratchPathsToSettle();
            if (uploadMutationMonitor is not null && uploadSnapshot is not null)
            {
                uploadMutationMonitor.ValidateNoChanges(
                    () => uploadSnapshot.ValidateUnchanged(
                        approvedSha256!,
                        allowExpectedScratchDirectoryMutation: true));
            }
            else
            {
                uploadMutationMonitor?.ValidateNoChanges();
                uploadSnapshot?.ValidateUnchanged(approvedSha256!);
            }
        }
        catch (Exception integrityFailure) when (uploadSnapshot is not null)
        {
            uploadMutationMonitor?.Dispose();
            AppleArchiveUploadEvidence? evidence = null;
            var detail = integrityFailure.Message;
            try
            {
                evidence = uploadSnapshot.RetainIntegrityFailure(detail);
            }
            catch (Exception diagnosticFailure)
            {
                detail += $" Failed input retained at '{uploadSnapshot.RootPath}'; diagnostic capture failed: {diagnosticFailure.Message}";
            }
            try
            {
                WriteAppleUploadIntegrityFailure(plan, app, result, detail, evidence);
            }
            catch (Exception checkpointFailure)
            {
                throw new AggregateException(
                    "Apple upload integrity failed and its rejection checkpoint could not be persisted. Do not upload again or qualify the remote build; inspect retained input and remote state.",
                    integrityFailure,
                    checkpointFailure);
            }
            throw new InvalidOperationException(detail, integrityFailure);
        }
    }
}
