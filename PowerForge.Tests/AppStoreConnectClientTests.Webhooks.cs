using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task WebhookClient_LimitStopsPaginationAtTheRequestedMaximum()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """
                {
                  "data": [{
                    "type": "webhooks",
                    "id": "webhook-1",
                    "attributes": { "name": "First", "enabled": true, "eventTypes": [] }
                  }],
                  "links": { "next": "https://api.appstoreconnect.apple.com/v1/apps/app-1/webhooks?cursor=next" }
                }
                """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var listed = await client.GetWebhooksAsync("app-1", limit: 1);

        Assert.Equal("webhook-1", Assert.Single(listed).Id);
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task WebhookClient_ListsCreatesUpdatesAndPings()
    {
        const string webhookJson = """
            {
              "data": {
                "type": "webhooks",
                "id": "webhook-1",
                "attributes": {
                  "name": "Release monitor",
                  "url": "https://release.example.test/apple",
                  "enabled": true,
                  "eventTypes": ["BUILD_UPLOAD_STATE_UPDATED"]
                }
              }
            }
            """;
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """
                {
                  "data": [{
                    "type": "webhooks",
                    "id": "webhook-1",
                    "attributes": {
                      "name": "Release monitor",
                      "url": "https://release.example.test/apple",
                      "enabled": true,
                      "eventTypes": ["BUILD_UPLOAD_STATE_UPDATED"]
                    }
                  }]
                }
                """),
            new SequenceResponse(HttpStatusCode.Created, webhookJson),
            new SequenceResponse(HttpStatusCode.OK, webhookJson),
            new SequenceResponse(HttpStatusCode.Created, "{ \"data\": { \"type\": \"webhookPings\", \"id\": \"ping-1\" } }"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var listed = await client.GetWebhooksAsync("app-1");
        var spec = new AppStoreConnectWebhookSpec
        {
            AppId = "app-1",
            Name = "Release monitor",
            Url = "https://release.example.test/apple",
            Secret = "a-strong-webhook-secret",
            EventTypes = new[] { "BUILD_UPLOAD_STATE_UPDATED" }
        };
        var created = await client.CreateWebhookAsync(spec);
        spec.AppId = string.Empty;
        var updated = await client.UpdateWebhookAsync("webhook-1", spec);
        await client.PingWebhookAsync("webhook-1");

        Assert.Equal("webhook-1", Assert.Single(listed).Id);
        Assert.Equal("webhook-1", created.Id);
        Assert.Equal("webhook-1", updated.Id);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, new HttpMethod("PATCH"), HttpMethod.Post }, handler.Methods);
        Assert.Contains("apps/app-1/webhooks", handler.RequestUris[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("\"secret\":\"a-strong-webhook-secret\"", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains("webhookPings", handler.RequestUris[3].ToString(), StringComparison.Ordinal);
    }
}
