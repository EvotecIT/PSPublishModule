using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class RepositoryDiagnosticPublishProcessTests
{
    [WindowsTheory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_publish_reports_saved_api_key_while_preserving_actual_authentication_behavior(bool v2)
    {
        using var host = new RepositoryDiagnosticProcessTests.IsolatedRepositoryHost();
        var port = LoopbackHttpListener.GetCandidatePort();
        using var listener = LoopbackHttpListener.Start(ref port);
        var root = $"http://127.0.0.1:{port}/";
        var index = root + (v2 ? "api/v2" : "v3/index.json");
        var publishSource = v2 ? index + "/package" : index;
        var password = "synthetic." + new string('x', 6400);
        const string apiKey = "SYNTHETIC_STORED_API_KEY";
        var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("publisher:" + password));
        var authentications = new ConcurrentQueue<string>();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException) when (!listener.IsListening) { break; }
                catch (ObjectDisposedException) { break; }
                await context.Request.InputStream.CopyToAsync(Stream.Null);
                var validCredential = context.Request.Headers["Authorization"] == basic;
                var validKey = context.Request.Headers["X-NuGet-ApiKey"] == apiKey;
                authentications.Enqueue(validKey ? "saved-api-key" : validCredential ? "supplied-credential" : "unauthenticated-or-wrong");
                string body = string.Empty;
                if (!validCredential && !validKey)
                {
                    context.Response.StatusCode = 401;
                    context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"synthetic\"";
                }
                else if (context.Request.HttpMethod == "GET" && context.Request.Url!.AbsolutePath == "/v3/index.json")
                {
                    context.Response.ContentType = "application/json";
                    body = $$"""{"version":"3.0.0","resources":[{"@id":"{{root}}upload","@type":"PackagePublish/2.0.0"}]}""";
                }
                else if (context.Request.HttpMethod == "PUT" && context.Request.Url!.AbsolutePath.TrimEnd('/') == (v2 ? "/api/v2/package" : "/upload")) context.Response.StatusCode = 201;
                else context.Response.StatusCode = 404;
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        try
        {
            var registered = host.RunScript($"Register-PSResourceRepository -Name SyntheticDiagnostics -Uri '{index}' -Trusted -Force -ErrorAction Stop");
            Assert.Equal(0, registered.ExitCode);
            var package = Path.Combine(host.Root, "Synthetic.Diagnostics.1.0.0.nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Synthetic.Diagnostics.nuspec").Open()))
                writer.Write("<package><metadata><id>Synthetic.Diagnostics</id><version>1.0.0</version><authors>Synthetic</authors><description>Local diagnostic contract</description></metadata></package>");
            var request = new RepositoryPublishRequest
            {
                Path = package, IsNupkg = true, RepositoryName = "SyntheticDiagnostics", Tool = PublishTool.PSResourceGet, SkipDependenciesCheck = true,
                Repository = new PublishRepositoryConfiguration { EnsureRegistered = false, Credential = new RepositoryCredential { UserName = "publisher", Secret = password } }
            };
            var publisher = new RepositoryPublisher(new NullLogger(), host);
            var first = publisher.Publish(request);
            Assert.NotNull(first.DiagnosticReport);
            Assert.True(first.DiagnosticReport.CredentialSupplied);
            Assert.Equal("Complete", first.DiagnosticReport.Runtime.SettingsInspection);
            Assert.Contains("supplied-credential", authentications);

            request.Repository.Credential!.Secret = "WRONG_SYNTHETIC_PASSWORD";
            var rejected = Assert.Throws<RepositoryPublishDiagnosticException>(() => publisher.Publish(request));
            Assert.Contains("HTTP 401", rejected.Message);
            Assert.True(rejected.Report.CredentialSupplied);

            // Seed a synthetic encrypted API key in this disposable identity's NuGet file, as nuget setapikey would.
            var seed = host.RunScript($$"""
                $ErrorActionPreference = 'Stop'
                Import-Module Microsoft.PowerShell.PSResourceGet
                $publisher = (Get-Command Publish-PSResource).ImplementingType.Assembly
                $reference = $publisher.GetReferencedAssemblies() | Where-Object Name -eq 'NuGet.Configuration' | Select-Object -First 1
                $nuget = [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($publisher).LoadFromAssemblyName($reference)
                $encrypt = $nuget.GetType('NuGet.Configuration.EncryptionUtility').GetMethod('EncryptString', [Reflection.BindingFlags]'Static,Public,NonPublic')
                $encrypted = $encrypt.Invoke($null, @('{{apiKey}}'))
                $file = Join-Path $env:APPDATA 'NuGet/NuGet.Config'
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
                [IO.File]::WriteAllText($file, '<configuration><packageSources><add key="SyntheticSource" value="{{publishSource}}"/></packageSources><apikeys><add key="{{publishSource}}" value="' + $encrypted + '" /></apikeys></configuration>')
                """);
            Assert.Equal(0, seed.ExitCode);
            var before = File.ReadAllText(host.NuGetConfig);
            host.Environment["NuGetPackageSourceCredentials_SyntheticSource"] = "Username=SYNTHETIC_ENV_USER;Password=SYNTHETIC_ENV_SECRET";
            var inspected = new RepositoryDiagnosticService(host).Inspect(new RepositoryDiagnosticRequest { RepositoryName = "SyntheticDiagnostics" });
            Assert.Equal(index, inspected.RegisteredUri);
            Assert.Equal(publishSource, inspected.PublishSourceUri);
            Assert.Contains(inspected.Findings, f => f.Code == "PFREPO001");
            Assert.Contains(inspected.Findings, f => f.Code == "PFREPO002");
            Assert.Equal(before, File.ReadAllText(host.NuGetConfig));
            host.Environment["NuGetPackageSourceCredentials_SyntheticSource"] = null;
            var third = publisher.Publish(request);
            Assert.Contains(third.DiagnosticReport!.Findings, f => f.Code == "PFREPO001");
            Assert.Contains("saved-api-key", authentications);
            Assert.Equal("Not reported by backend", third.DiagnosticReport.AuthenticationSelection);
            var exported = JsonSerializer.Serialize(third.DiagnosticReport);
            Assert.DoesNotContain(apiKey, exported);
            Assert.DoesNotContain(password, exported);
            Assert.DoesNotContain("WRONG_SYNTHETIC_PASSWORD", exported);
        }
        finally
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
