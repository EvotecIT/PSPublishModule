using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int CommandAppleReviewNotes(string operation, string[] argv, string configPath, bool outputJson, ILogger logger)
    {
        var spec = CliJson.DeserializeOrThrow(File.ReadAllText(configPath), CliJson.Context.AppStoreConnectReviewNotesSpec, configPath);
        AppStoreConnectReviewNotesService.ValidateSpec(spec);
        if (operation == "notes-validate")
        {
            WriteAppleReviewDetailsOutput(operation, new { valid = true, spec.Target }, true, outputJson, logger);
            return 0;
        }
        var credential = ResolveAppleGovernanceCredential(argv, configPath);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReviewNotesService(client);
        AppStoreConnectReviewNotesPlan plan;
        if (operation == "notes-plan")
            plan = service.PlanAsync(spec).GetAwaiter().GetResult();
        else
        {
            if (!argv.Any(value => value.Equals("--confirm", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Review-notes apply requires --confirm after reviewing a plan receipt.");
            var reviewedPath = ResolveExistingFilePath(TryGetOptionValue(argv, "--reviewed-plan")
                ?? throw new InvalidOperationException("Review-notes apply requires --reviewed-plan."));
            var reviewed = CliJson.DeserializeOrThrow(File.ReadAllText(reviewedPath), CliJson.Context.AppStoreConnectReviewNotesPlan, reviewedPath);
            plan = service.ApplyAsync(spec, reviewed, confirmApply: true).GetAwaiter().GetResult();
        }
        var receipt = ResolveAppleReviewDetailsReceiptPath(argv, configPath,
            operation == "notes-plan" ? "review-notes-plan.json" : "review-notes-receipt.json");
        WriteJsonFile(receipt, plan, CliJson.Context.AppStoreConnectReviewNotesPlan);
        if (outputJson)
            WriteAppleReviewDetailsJson(operation, true, 0, CliJson.SerializeToElement(plan, CliJson.Context.AppStoreConnectReviewNotesPlan), null);
        else
            logger.Info($"App Review notes: version={plan.VersionString}; converged={plan.IsConverged}; receipt={receipt}.");
        return 0;
    }
}
