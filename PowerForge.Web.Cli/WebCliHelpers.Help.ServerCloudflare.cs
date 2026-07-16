using System;

namespace PowerForge.Web.Cli;

internal static partial class WebCliHelpers
{
    private static void PrintServerAndCloudflareUsage()
    {
        Console.WriteLine("  powerforge-web server inspect --manifest <serverrecovery.json> [--fail-on-drift] [--output json]");
        Console.WriteLine("  powerforge-web server plan --manifest <serverrecovery.json> [--output json]");
        Console.WriteLine("  powerforge-web server validate --manifest <serverrecovery.json> [--output json] (alias for plan)");
        Console.WriteLine("  powerforge-web server capture --manifest <serverrecovery.json> [--out <dir>] [--dry-run] [--skip-files] [--skip-encrypted] [--encrypt-remote] [--fail-on-failure] [--output json]");
        Console.WriteLine("  powerforge-web server deploy --manifest <serverrecovery.json> [--dry-run] [--fail-on-failure] [--output json]");
        Console.WriteLine("  powerforge-web server verify --manifest <serverrecovery.json> [--fail-on-failure] [--url-timeout-seconds <n>] [--output json]");
        Console.WriteLine("  powerforge-web server scaffold --domain <domain> --repository <owner/repo> --repository-ref <sha> --engine-ref <sha> --host <host> --backup-repository <owner/repo> --backup-recipient <age1...> [--branch <name>] [--website-root <dir>] [--ssh-port <n>] [--site-id <id>] [--smoke-paths <paths>] [--private-repository] [--www] [--cloudflare] [--out <dir>] [--force] [--output json]");
        Console.WriteLine("  powerforge-web server bootstrap-plan --manifest <serverrecovery.json> [--out <dir>] [--output json]");
        Console.WriteLine("  powerforge-web server restore-secrets-plan --manifest <serverrecovery.json> [--out <dir>] [--archive <encrypted-secrets.tar.gz.age>] [--output json]");
        Console.WriteLine("  powerforge-web cloudflare purge --zone-id <id> [--token <token> | --token-env <env>]");
        Console.WriteLine("                     [--purge-everything] [--site-config <site.json>] [--base-url <url>] [--path <p[,p...]>] [--url <u[,u...]>] [--dry-run]");
        Console.WriteLine("  powerforge-web cloudflare verify [--site-config <site.json>] [--base-url <url>] [--path <p[,p...]>] [--url <u[,u...]>]");
        Console.WriteLine("                     [--warmup <n>] [--allow-status <HIT,REVALIDATED,EXPIRED,STALE>] [--timeout-ms <n>]");
        Console.WriteLine("  powerforge-web cloudflare cache-policy apply --zone-id <id> [--token <token> | --token-env <env>]");
        Console.WriteLine("                     [--site-config <site.json> | --hostname <host>] [--base-path <path>] [--policy-name <name>]");
        Console.WriteLine("                     [--html-path <p[,p...]>] [--dry-run]");
    }
}
