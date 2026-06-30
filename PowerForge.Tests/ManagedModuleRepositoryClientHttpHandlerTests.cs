using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryClientHttpHandlerTests
{
    [Fact]
    public void CreateDefaultHttpMessageHandler_applies_explicit_proxy_options()
    {
        var proxyAddress = new Uri("http://proxy.example.test:8080");
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                ProxyAddress = proxyAddress,
                BypassProxyOnLocal = false,
                ProxyCredential = new RepositoryCredential
                {
                    UserName = "proxy-user",
                    Secret = "proxy-secret"
                }
            }));

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Equal(proxyAddress, handler.Proxy!.GetProxy(new Uri("https://example.test/v3/index.json")));
        var credential = Assert.IsType<NetworkCredential>(handler.Proxy.Credentials);
        Assert.Equal("proxy-user", credential.UserName);
        Assert.Equal("proxy-secret", credential.Password);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_can_disable_proxy()
    {
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                UseProxy = false,
                ProxyAddress = new Uri("http://proxy.example.test:8080")
            }));

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_applies_connection_limit_policy()
    {
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                MaxConnectionsPerServer = 48
            }));

        Assert.Equal(48, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_keeps_at_least_one_connection_per_server()
    {
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                MaxConnectionsPerServer = 0
            }));

        Assert.Equal(1, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_requests_compressed_repository_responses()
    {
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions()));

        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.GZip));
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Deflate));
#if !NET472
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Brotli));
#endif
    }

    [Fact]
    public void RedirectRequest_keeps_credentials_only_for_same_origin_redirects()
    {
        var method = typeof(ManagedModuleRepositoryClient).GetMethod(
            "CreateRedirectRequest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        using var source = new HttpRequestMessage(HttpMethod.Get, "https://feed.example.test:8443/packages");
        source.Headers.Authorization = new AuthenticationHeaderValue("Basic", "secret");
        source.Headers.Add("X-NuGet-ApiKey", "api-secret");

        using var sameOrigin = Assert.IsType<HttpRequestMessage>(method!.Invoke(null, new object[]
        {
            source,
            new Uri("https://feed.example.test:8443/redirected")
        }));
        using var downgraded = Assert.IsType<HttpRequestMessage>(method.Invoke(null, new object[]
        {
            source,
            new Uri("http://feed.example.test:8443/redirected")
        }));
        using var differentPort = Assert.IsType<HttpRequestMessage>(method.Invoke(null, new object[]
        {
            source,
            new Uri("https://feed.example.test/redirected")
        }));

        Assert.NotNull(sameOrigin.Headers.Authorization);
        Assert.True(sameOrigin.Headers.Contains("X-NuGet-ApiKey"));
        Assert.Null(downgraded.Headers.Authorization);
        Assert.False(downgraded.Headers.Contains("X-NuGet-ApiKey"));
        Assert.Null(differentPort.Headers.Authorization);
        Assert.False(differentPort.Headers.Contains("X-NuGet-ApiKey"));
    }
}
