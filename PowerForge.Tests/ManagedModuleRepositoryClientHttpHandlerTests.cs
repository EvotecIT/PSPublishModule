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
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                ProxyAddress = proxyAddress,
                BypassProxyOnLocal = false,
                ProxyCredential = new RepositoryCredential
                {
                    UserName = "proxy-user",
                    Secret = "proxy-secret"
                }
            });

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
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
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                UseProxy = false,
                ProxyAddress = new Uri("http://proxy.example.test:8080")
            });

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_applies_connection_limit_policy()
    {
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                MaxConnectionsPerServer = 48
            });

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
        Assert.Equal(48, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_keeps_at_least_one_connection_per_server()
    {
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                MaxConnectionsPerServer = 0
            });

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
        Assert.Equal(1, handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_requests_compressed_repository_responses()
    {
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(new ManagedModuleRepositoryClientOptions());

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.GZip));
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Deflate));
#if !NET472
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Brotli));
#endif
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_disables_automatic_redirects_for_repository_policy()
    {
        var rawHandler = ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(new ManagedModuleRepositoryClientOptions());

#if NET472
        var handler = Assert.IsType<HttpClientHandler>(rawHandler);
#else
        var handler = Assert.IsType<SocketsHttpHandler>(rawHandler);
#endif
        Assert.False(handler.AllowAutoRedirect);
    }

#if !NET472
    [Fact]
    public void CreateDefaultHttpMessageHandler_enables_multiple_http2_connections_on_modern_runtime()
    {
        var handler = Assert.IsType<SocketsHttpHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions()));

        Assert.True(handler.EnableMultipleHttp2Connections);
        Assert.Equal(16 * 1024 * 1024, handler.InitialHttp2StreamWindowSize);
    }

    [Fact]
    public void CreateDefaultHttpClient_prefers_http2_with_http11_fallback_on_modern_runtime()
    {
        using var client = ManagedModuleRepositoryClient.CreateDefaultHttpClient(new ManagedModuleRepositoryClientOptions());

        Assert.Equal(HttpVersion.Version20, client.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, client.DefaultVersionPolicy);
    }
#endif

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
