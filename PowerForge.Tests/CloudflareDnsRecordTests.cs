using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareDnsRecordTests
{
    private const string ZoneId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Apply_ShouldCreateMissingRecord()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Zone() })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray())),
            JsonResponse(HttpStatusCode.OK, Envelope(Record("record-created", "141.94.123.4", proxied: true))));
        using var client = NewClient(handler);

        var result = CloudflareDnsRecordManager.Apply(
            "evotec.xyz",
            "secret-token",
            "A",
            "control.evotec.xyz",
            "141.94.123.4",
            proxied: true,
            ttl: 1,
            comment: "Managed by PowerForge",
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal("created", result.Action);
        Assert.Equal("record-created", result.RecordId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.EndsWith($"zones/{ZoneId}/dns_records", handler.Requests[2].PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"141.94.123.4\"", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Contains("\"proxied\":true", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", string.Join('\n', handler.Requests.Select(request => request.Body)), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldUpdateDriftedRecord()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Zone() })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Record("record-existing", "192.0.2.10", proxied: false) })),
            JsonResponse(HttpStatusCode.OK, Envelope(Record("record-existing", "141.94.123.4", proxied: true))));
        using var client = NewClient(handler);

        var result = Apply(client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ChangesRequired);
        Assert.True(result.Changed);
        Assert.Equal("updated", result.Action);
        Assert.Equal(HttpMethod.Patch, handler.Requests[2].Method);
        Assert.EndsWith($"zones/{ZoneId}/dns_records/record-existing", handler.Requests[2].PathAndQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("tags", handler.Requests[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldRemainIdempotentWhenRecordMatches()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Zone() })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Record("record-existing", "141.94.123.4", proxied: true) })));
        using var client = NewClient(handler);

        var result = Apply(client);

        Assert.True(result.Success, result.Message);
        Assert.False(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Equal("unchanged", result.Action);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Apply_DryRunShouldReportCreationWithoutMutation()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Zone() })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray())));
        using var client = NewClient(handler);

        var result = CloudflareDnsRecordManager.Apply(
            "evotec.xyz",
            "secret-token",
            "A",
            "control.evotec.xyz",
            "141.94.123.4",
            proxied: true,
            ttl: 1,
            comment: "Managed by PowerForge",
            dryRun: true,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DryRun);
        Assert.True(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Equal("would-create", result.Action);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Apply_ShouldRefuseAmbiguousRecords()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { Zone() })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray
            {
                Record("record-one", "141.94.123.4", proxied: true),
                Record("record-two", "141.94.123.4", proxied: true)
            })));
        using var client = NewClient(handler);

        var result = Apply(client);

        Assert.False(result.Success);
        Assert.Contains("multiple", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Apply_ShouldNormalizeInternationalizedNamesToPunycode()
    {
        const string internationalZoneId = "abcdef0123456789abcdef0123456789";
        var existing = new JsonObject
        {
            ["id"] = "record-idn",
            ["type"] = "CNAME",
            ["name"] = "control.xn--tst-qla.example",
            ["content"] = "target.xn--tst-qla.example",
            ["proxied"] = true,
            ["ttl"] = 1,
            ["comment"] = "Managed by PowerForge"
        };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray
            {
                new JsonObject { ["id"] = internationalZoneId, ["name"] = "xn--tst-qla.example", ["status"] = "active" }
            })),
            JsonResponse(HttpStatusCode.OK, Envelope(new JsonArray { existing })));
        using var client = NewClient(handler);

        var result = CloudflareDnsRecordManager.Apply(
            "täst.example",
            "secret-token",
            "CNAME",
            "control.täst.example",
            "target.täst.example",
            proxied: true,
            ttl: 1,
            comment: "Managed by PowerForge",
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.Equal("unchanged", result.Action);
        Assert.Equal("control.xn--tst-qla.example", result.RecordName);
        Assert.Equal("target.xn--tst-qla.example", result.RecordContent);
        Assert.Contains("name=xn--tst-qla.example", handler.Requests[0].PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("name=control.xn--tst-qla.example", handler.Requests[1].PathAndQuery, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other.example", "recordName 'other.example' must belong to zone 'evotec.xyz'.")]
    [InlineData("control.evotec.xyz", "recordContent must be a valid IPv4 address for an A record.", "2001:db8::1")]
    public void Apply_ShouldValidateScopeAndContentBeforeCallingCloudflare(string recordName, string expectedMessage, string content = "141.94.123.4")
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);

        var result = CloudflareDnsRecordManager.Apply(
            "evotec.xyz",
            "secret-token",
            "A",
            recordName,
            content,
            proxied: true,
            ttl: 1,
            comment: null,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.Success);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void CompositeAction_ShouldKeepTokenOutOfArgumentsAndRejectPullRequests()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-dns-record", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-cloudflare-dns-record", "Invoke-PowerForgeCloudflareDnsRecord.ps1");

        Assert.Contains("Reject pull request DNS changes", action, StringComparison.Ordinal);
        Assert.Contains("Zone > DNS > Edit and Zone > Zone > Read", action, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeCloudflareDnsRecord.ps1", action, StringComparison.Ordinal);
        Assert.Contains("--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--token', $env:POWERFORGE_CLOUDFLARE_API_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("'dns-record'", script, StringComparison.Ordinal);
        Assert.True(script.Split('\n').Length < 100, "The action entrypoint should remain a bounded adapter over the CLI.");
    }

    [Fact]
    public void Command_ShouldRejectUnknownDnsWriteArgumentsBeforeUsingDefaults()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "cloudflare",
            ["dns-record", "apply", "--proxie", "false"],
            outputJson: false,
            new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    private static CloudflareDnsRecordApplyResult Apply(HttpClient client) => CloudflareDnsRecordManager.Apply(
        "evotec.xyz",
        "secret-token",
        "A",
        "control.evotec.xyz",
        "141.94.123.4",
        proxied: true,
        ttl: 1,
        comment: "Managed by PowerForge",
        dryRun: false,
        logger: null,
        client);

    private static JsonObject Zone() => new()
    {
        ["id"] = ZoneId,
        ["name"] = "evotec.xyz",
        ["status"] = "active"
    };

    private static JsonObject Record(string id, string content, bool proxied) => new()
    {
        ["id"] = id,
        ["type"] = "A",
        ["name"] = "control.evotec.xyz",
        ["content"] = content,
        ["proxied"] = proxied,
        ["ttl"] = 1,
        ["comment"] = "Managed by PowerForge",
        ["tags"] = new JsonArray("manual-metadata")
    };

    private static string Envelope(JsonNode result) => new JsonObject
    {
        ["success"] = true,
        ["result"] = result
    }.ToJsonString();

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpClient NewClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.cloudflare.test/client/v4/")
    };

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PSPublishModule.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return File.ReadAllText(Path.Combine([current!.FullName, .. segments]));
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        internal List<CapturedRequest> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            CaptureAndRespond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.PathAndQuery.TrimStart('/'), body));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Unexpected HTTP request.");
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string PathAndQuery, string Body);
}
