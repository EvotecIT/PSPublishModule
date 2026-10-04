using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string CatalogUpdateUsage = "Usage: powerforge release catalog <prepare|submit|status|reserve|reconcile> --config <catalog.json> --out <directory> [--manifest <release-manifest.json> --checksums <SHA256SUMS.txt> --asset-root <downloads> --delivery-release <immutable-release-id>] [--channel <winget|store|all>] [--store-config <store.json>] [--execute] [--reservation-key <fresh-guid-N>] [--reference <PR-url|submission-id|none> --confirm-reconciled] [--output json]";

    private static int CommandCatalogUpdate(string[] args, ILogger logger)
    {
        var json = IsJsonOutput(args);
        try
        {
            if (args.Any(value => value is "--help" or "-h"))
            {
                if (json) WriteJson(new CliJsonEnvelope { Command = "release.catalog", Success = true, Result = CliJson.SerializeToElement(CatalogUpdateUsage, CliJson.Context.String) });
                else Console.WriteLine(CatalogUpdateUsage);
                return 0;
            }
            var action = args.FirstOrDefault();
            if (action is not ("prepare" or "submit" or "status" or "reconcile" or "reserve")) throw new ArgumentException(CatalogUpdateUsage);
            var allowed = new[] { "--config", "--out", "--manifest", "--checksums", "--asset-root", "--delivery-release", "--resume-from", "--channel", "--store-config", "--execute", "--output", "--reference", "--confirm-reconciled", "--reservation-key", "--require-authentication" };
            foreach (var value in args.Where(value => value.StartsWith("--", StringComparison.Ordinal)))
                if (!allowed.Contains(value, StringComparer.Ordinal)) throw new ArgumentException("Unknown catalog option: " + value);
            if (action != "submit" && args.Contains("--execute")) throw new ArgumentException("--execute is only valid with submit.");
            string Required(string name) => TryGetOptionValue(args, name) ?? throw new ArgumentException("Missing " + name + ". " + CatalogUpdateUsage);
            var profilePath = Path.GetFullPath(Required("--config"));
            var profile = JsonSerializer.Deserialize(File.ReadAllText(profilePath), ReleaseCatalogJsonContext.Default.CatalogUpdateSpec)
                ?? throw new InvalidOperationException("Missing catalog profile.");
            var basePath = Path.GetDirectoryName(profilePath)!;
            var release = LoadPowerForgeReleaseSpecWithPath(Path.Combine(basePath, profile.ReleaseConfigPath));
            var output = Path.GetFullPath(Required("--out"));
            var service = new CatalogUpdateService();
            CatalogUpdateReceipt result;
            if (action == "prepare")
                result = service.PrepareAsync(profile, profilePath, release.Value, release.FullPath, Required("--manifest"),
                    Required("--checksums"), Required("--asset-root"), output, Required("--delivery-release"),
                    resumeFrom: TryGetOptionValue(args, "--resume-from")).GetAwaiter().GetResult();
            else if (action == "reconcile")
                result = service.Reconcile(output, profilePath, release.FullPath, Required("--channel"), Required("--reference"), args.Contains("--confirm-reconciled"),
                    TryGetOptionValue(args, "--store-config") ?? (profile.StoreConfigPath is null ? null : Path.Combine(basePath, profile.StoreConfigPath)));
            else if (action == "reserve")
                result = service.Reserve(output, profilePath, release.FullPath, Required("--channel"), Required("--reservation-key"),
                    TryGetOptionValue(args, "--store-config") ?? (profile.StoreConfigPath is null ? null : Path.Combine(basePath, profile.StoreConfigPath)));
            else
            {
                var storePath = TryGetOptionValue(args, "--store-config") ??
                    (profile.StoreConfigPath is null ? null : Path.Combine(basePath, profile.StoreConfigPath));
                StoreSubmissionSpec? store = null;
                if (storePath is not null) { var loaded = LoadStoreSubmissionSpecWithPath(storePath); store = loaded.Value; storePath = loaded.FullPath; }
                result = action == "status"
                    ? service.StatusAsync(profile, profilePath, release.FullPath, output, store, storePath).GetAwaiter().GetResult()
                    : service.SubmitAsync(profile, profilePath, release.Value, release.FullPath, output,
                        TryGetOptionValue(args, "--channel") ?? "all", store, storePath, args.Contains("--execute"), reservationKey: TryGetOptionValue(args, "--reservation-key"),
                        requireAuthentication: args.Contains("--require-authentication")).GetAwaiter().GetResult();
            }
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.catalog." + action,
                Success = true, ExitCode = 0, Result = CliJson.SerializeToElement(result, CliJson.Context.CatalogUpdateReceipt) });
            else
            {
                logger.Success($"{result.PackageIdentifier} {result.PackageVersion}: {Path.Combine(output, "catalog-update.json")}");
                logger.Info($"WinGet: {result.Winget.State} {result.Winget.Reference}; Store: {result.Store.State} {result.Store.Reference} {result.Store.RemoteStatus}");
                if (action == "submit" && !args.Contains("--execute")) logger.Info("Preflight passed. Add --execute to submit the selected channels.");
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.catalog", Success = false, ExitCode = 2, Error = exception.Message });
            else logger.Error(exception.Message);
            return 2;
        }
    }
}
