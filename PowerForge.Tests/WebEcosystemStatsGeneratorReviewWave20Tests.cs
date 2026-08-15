using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebEcosystemStatsGeneratorTests
{
    [Fact]
    public void Generate_PaginatesPowerShellGalleryByRawEntriesBeforeOwnerFiltering()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-psgallery-pagination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = WebEcosystemStatsGenerator.Generate(new WebEcosystemStatsOptions
            {
                OutputPath = Path.Combine(root, "stats.json"),
                PowerShellGalleryOwner = "ExpectedOwner",
                PowerShellGalleryAuthor = "Shared Author",
                MaxItems = 1
            }, new PowerShellGalleryOwnerPaginationHandler());

            Assert.Equal(1, result.PowerShellGalleryModuleCount);
            using var document = JsonDocument.Parse(File.ReadAllText(result.OutputPath));
            var modules = document.RootElement.GetProperty("powerShellGallery").GetProperty("modules");
            var module = Assert.Single(modules.EnumerateArray().ToArray());
            Assert.Equal("Owned.Module", module.GetProperty("id").GetString());
            Assert.Equal("ExpectedOwner", module.GetProperty("owners").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_StopsPowerShellGalleryPaginationWhenAFilteredPageRepeats()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-psgallery-repeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new RepeatedPowerShellGalleryPageHandler();
            var result = WebEcosystemStatsGenerator.Generate(new WebEcosystemStatsOptions
            {
                OutputPath = Path.Combine(root, "stats.json"),
                PowerShellGalleryOwner = "ExpectedOwner",
                PowerShellGalleryAuthor = "Shared Author",
                MaxItems = 1
            }, handler);

            Assert.Equal(0, result.PowerShellGalleryModuleCount);
            Assert.Equal(6, handler.RequestCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("repeated page", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PowerShellGalleryOwnerPaginationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            var skipMatch = Regex.Match(query, @"(?:^|&)\$skip=(?<skip>\d+)");
            var takeMatch = Regex.Match(query, @"(?:^|&)\$top=(?<take>\d+)");
            var skip = skipMatch.Success ? int.Parse(skipMatch.Groups["skip"].Value) : 0;
            var take = takeMatch.Success ? int.Parse(takeMatch.Groups["take"].Value) : 100;
            var entries = string.Concat(
                Enumerable.Range(0, 100)
                    .Select(index => (Id: $"Spoofed.Module.{index}", Owner: "Attacker"))
                    .Append((Id: "Owned.Module", Owner: "ExpectedOwner"))
                    .Skip(skip)
                    .Take(take)
                    .Select(static module => Entry(module.Id, module.Owner)));
            var feed = $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <feed xmlns="http://www.w3.org/2005/Atom"
                      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                  {{entries}}
                </feed>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(feed, Encoding.UTF8, "application/atom+xml")
            });
        }
    }

    private sealed class RepeatedPowerShellGalleryPageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var entries = string.Concat(Enumerable.Range(0, 100).Select(index => Entry($"Spoofed.Module.{index}", "Attacker")));
            var feed = $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <feed xmlns="http://www.w3.org/2005/Atom"
                      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                  {{entries}}
                </feed>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(feed, Encoding.UTF8, "application/atom+xml")
            });
        }
    }

    private static string Entry(string id, string owner)
        => $$"""
             <entry>
               <m:properties>
                 <d:Id>{{id}}</d:Id>
                 <d:Version>1.0.0</d:Version>
                 <d:Authors>Shared Author</d:Authors>
                 <d:Owners>{{owner}}</d:Owners>
                 <d:DownloadCount>1</d:DownloadCount>
               </m:properties>
             </entry>
             """;
}
