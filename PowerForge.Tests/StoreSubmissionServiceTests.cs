using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class StoreSubmissionServiceTests
{
    [Fact]
    public void Plan_PrefersUploadArtifactsFromSourceDirectory()
    {
        using var scope = new TemporaryScope();
        var sourceDirectory = Path.Combine(scope.RootPath, "store");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "Contoso.msixbundle"), "bundle", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(sourceDirectory, "Contoso.msixupload"), "upload", new UTF8Encoding(false));

        var spec = CreateSpec(new StoreSubmissionTarget
        {
            Name = "Contoso",
            ApplicationId = "12345678",
            SourceDirectory = sourceDirectory
        });

        using var service = new StoreSubmissionService(new NullLogger());
        var plan = service.Plan(spec, Path.Combine(scope.RootPath, "store.submit.json"));

        Assert.Single(plan.PackageFiles);
        Assert.EndsWith("Contoso.msixupload", plan.PackageFiles[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_CreatesUpdatesUploadsAndCommitsSubmission()
    {
        using var scope = new TemporaryScope();
        var packagePath = CreatePackageFile(scope.RootPath, "Contoso.msixupload");
        var requests = new List<RecordedRequest>();
        var statusCalls = 0;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, recorded) =>
        {
            requests.Add(recorded);

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions")
            {
                return JsonResponse("""
                    {
                      "id": "draft-001",
                      "status": "PendingCommit",
                      "fileUploadUrl": "https://upload.contoso.test/blob?sas=abc",
                      "applicationPackages": []
                    }
                    """);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions/draft-001")
            {
                return JsonResponse(recorded.BodyText ?? "{}");
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.Host == "upload.contoso.test")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions/draft-001/commit")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions/draft-001/status")
            {
                statusCalls++;
                return JsonResponse(statusCalls switch
                {
                    1 => """{ "status": "CommitStarted", "statusDetails": { "errors": [], "warnings": [] } }""",
                    2 => """{ "status": "PreProcessing", "statusDetails": { "errors": [], "warnings": [] } }""",
                    _ => """{ "status": "Published", "statusDetails": { "errors": [], "warnings": [] } }"""
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("unexpected request")
            };
        }));

        var spec = CreateSpec(new StoreSubmissionTarget
        {
            Name = "Contoso",
            ApplicationId = "12345678",
            PackagePaths = new[] { packagePath },
            ZipPath = Path.Combine(scope.RootPath, "Artifacts", "submission.zip")
        }, accessToken: "test-token");

        using var service = new StoreSubmissionService(new NullLogger(), client);
        var result = await service.RunAsync(spec, Path.Combine(scope.RootPath, "store.submit.json"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.CreatedSubmission);
        Assert.True(result.UploadedPackageArchive);
        Assert.True(result.CommittedSubmission);
        Assert.Equal("draft-001", result.SubmissionId);
        Assert.Equal("Published", result.FinalStatus);

        var updateRequest = Assert.Single(
            requests,
            entry => entry.Method == HttpMethod.Put &&
                     entry.Uri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions/draft-001");
        using var updateJson = JsonDocument.Parse(updateRequest.BodyText!);
        var packages = updateJson.RootElement.GetProperty("applicationPackages");
        Assert.Equal(1, packages.GetArrayLength());
        var package = packages[0];
        Assert.Equal("Contoso.msixupload", package.GetProperty("fileName").GetString());
        Assert.Equal("PendingUpload", package.GetProperty("fileStatus").GetString());
        Assert.Equal("None", package.GetProperty("minimumDirectXVersion").GetString());
        Assert.Equal("None", package.GetProperty("minimumSystemRam").GetString());

        Assert.NotNull(result.PackageZipPath);
        Assert.True(File.Exists(result.PackageZipPath));
        using var archive = ZipFile.OpenRead(result.PackageZipPath!);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("Contoso.msixupload", entry.FullName);
    }

    [Fact]
    public async Task RunAsync_UsesClientCredentialsWhenAccessTokenIsNotConfigured()
    {
        using var scope = new TemporaryScope();
        var packagePath = CreatePackageFile(scope.RootPath, "Contoso.msixupload");
        var requests = new List<RecordedRequest>();

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, recorded) =>
        {
            requests.Add(recorded);

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://login.microsoftonline.com/tenant-001/oauth2/token")
            {
                return JsonResponse("""{ "access_token": "oauth-token", "expires_in": 3600 }""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions")
            {
                return JsonResponse("""
                    {
                      "id": "draft-002",
                      "status": "PendingCommit",
                      "fileUploadUrl": "https://upload.contoso.test/blob?sas=def",
                      "applicationPackages": []
                    }
                    """);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsoluteUri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions/draft-002")
            {
                return JsonResponse(recorded.BodyText ?? "{}");
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.Host == "upload.contoso.test")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("unexpected request")
            };
        }));

        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "Contoso",
                    ApplicationId = "12345678",
                    PackagePaths = new[] { packagePath },
                    ZipPath = Path.Combine(scope.RootPath, "Artifacts", "submission.zip"),
                    Commit = false
                }
            }
        };

        using var service = new StoreSubmissionService(new NullLogger(), client);
        var result = await service.RunAsync(spec, Path.Combine(scope.RootPath, "store.submit.json"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("PendingCommit", result.FinalStatus);
        Assert.Contains(requests, request => request.Uri == "https://login.microsoftonline.com/tenant-001/oauth2/token");
        Assert.Contains(requests, request => request.Uri == "https://manage.devcenter.microsoft.com/v1.0/my/applications/12345678/submissions"
            && request.Authorization == "Bearer oauth-token");
    }

    [Fact]
    public async Task RunAsync_DesktopInstaller_UpdatesPackagesAndCreatesSubmission()
    {
        var handler = new RecordingHttpMessageHandler((request, recorded) =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://login.microsoftonline.com/tenant-001/oauth2/v2.0/token")
            {
                return JsonResponse("""{ "access_token": "desktop-token", "expires_in": 3600 }""");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/status")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "isReady": true, "ongoingSubmissionId": "" } }""");
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/packages")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "pollingUrl": "/submission/v1/product/desktop-123/status", "ongoingSubmissionId": "" } }""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/packages/commit")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "pollingUrl": "/submission/v1/product/desktop-123/status", "ongoingSubmissionId": "" } }""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/submit")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "submissionId": "desktop-submission-1" } }""");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/submission/desktop-submission-1/status")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "publishingStatus": "PUBLISHED", "hasFailed": false } }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("unexpected request")
            };
        });
        using var client = new HttpClient(handler);

        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                SellerId = "seller-001",
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "DesktopContoso",
                    Provider = StoreSubmissionProviderKind.DesktopInstaller,
                    ApplicationId = "desktop-123",
                    DesktopPackages = new[]
                    {
                        new StoreSubmissionDesktopPackage
                        {
                            PackageUrl = "https://downloads.contoso.test/PowerForgeSetup.msi",
                            Languages = new[] { "en-us" },
                            Architectures = new[] { "X64" },
                            IsSilentInstall = true,
                            PackageType = "msi"
                        }
                    }
                }
            }
        };

        using var service = new StoreSubmissionService(new NullLogger(), client);
        var result = await service.RunAsync(spec, Path.Combine(Path.GetTempPath(), "desktop-store-submit.json"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("desktop-submission-1", result.SubmissionId);
        Assert.Equal("PUBLISHED", result.FinalStatus);
        Assert.True(result.CommittedSubmission);

        var updateRequest = Assert.Single(
            handler.Requests,
            entry => entry.Method == HttpMethod.Put &&
                     entry.Uri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/packages");
        Assert.Equal("Bearer desktop-token", updateRequest.Authorization);
        Assert.Equal("seller-001", updateRequest.SellerId);

        using var updateJson = JsonDocument.Parse(updateRequest.BodyText!);
        var packages = updateJson.RootElement.GetProperty("packages");
        Assert.Equal(1, packages.GetArrayLength());
        var package = packages[0];
        Assert.Equal("https://downloads.contoso.test/PowerForgeSetup.msi", package.GetProperty("packageUrl").GetString());
        Assert.Equal("msi", package.GetProperty("packageType").GetString());
    }

    [Fact]
    public async Task RunAsync_DesktopInstaller_NoCommitStopsBeforeSubmission()
    {
        var handler = new RecordingHttpMessageHandler((request, recorded) =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://login.microsoftonline.com/tenant-001/oauth2/v2.0/token")
            {
                return JsonResponse("""{ "access_token": "desktop-token", "expires_in": 3600 }""");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/status")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "isReady": true, "ongoingSubmissionId": "" } }""");
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/packages")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "pollingUrl": "/submission/v1/product/desktop-123/status", "ongoingSubmissionId": "" } }""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsoluteUri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/packages/commit")
            {
                return JsonResponse("""{ "isSuccess": true, "errors": [], "responseData": { "pollingUrl": "/submission/v1/product/desktop-123/status", "ongoingSubmissionId": "" } }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("unexpected request")
            };
        });
        using var client = new HttpClient(handler);

        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                SellerId = "seller-001",
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "DesktopContoso",
                    Provider = StoreSubmissionProviderKind.DesktopInstaller,
                    ApplicationId = "desktop-123",
                    Commit = false,
                    DesktopPackages = new[]
                    {
                        new StoreSubmissionDesktopPackage
                        {
                            PackageUrl = "https://downloads.contoso.test/PowerForgeSetup.msi",
                            Languages = new[] { "en-us" },
                            Architectures = new[] { "X64" },
                            IsSilentInstall = true,
                            PackageType = "msi"
                        }
                    }
                }
            }
        };

        using var service = new StoreSubmissionService(new NullLogger(), client);
        var result = await service.RunAsync(spec, Path.Combine(Path.GetTempPath(), "desktop-store-submit.json"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("READY", result.FinalStatus);
        Assert.Null(result.SubmissionId);
        Assert.False(result.CommittedSubmission);
        Assert.DoesNotContain(
            handler.Requests,
            entry => entry.Method == HttpMethod.Post &&
                     entry.Uri == "https://api.store.microsoft.com/submission/v1/product/desktop-123/submit");
    }

    [Fact]
    public void RedactSecrets_ClearsInlineAuthenticationSecrets()
    {
        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001",
                ClientSecretEnvVar = "PARTNER_CENTER_CLIENT_SECRET",
                AccessToken = "token-001",
                AccessTokenEnvVar = "PARTNER_CENTER_ACCESS_TOKEN"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "Contoso",
                    ApplicationId = "12345678"
                }
            }
        };

        var sanitized = StoreSubmissionSpecSanitizer.RedactSecrets(spec);

        Assert.Null(sanitized.Authentication.ClientSecret);
        Assert.Null(sanitized.Authentication.AccessToken);
        Assert.Equal("PARTNER_CENTER_CLIENT_SECRET", sanitized.Authentication.ClientSecretEnvVar);
        Assert.Equal("PARTNER_CENTER_ACCESS_TOKEN", sanitized.Authentication.AccessTokenEnvVar);
        Assert.Equal("secret-001", spec.Authentication.ClientSecret);
        Assert.Equal("token-001", spec.Authentication.AccessToken);
    }

    [Fact]
    public void RedactSecrets_AllowsMissingAuthentication()
    {
        var spec = new StoreSubmissionSpec
        {
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "Contoso",
                    ApplicationId = "12345678"
                }
            }
        };

        var sanitized = StoreSubmissionSpecSanitizer.RedactSecrets(spec);

        Assert.NotNull(sanitized.Authentication);
        Assert.Null(sanitized.Authentication.ClientSecret);
        Assert.Null(sanitized.Authentication.AccessToken);
        Assert.Single(sanitized.Targets);
    }

    [Fact]
    public void Validate_RejectsNonHttpsAuthorityHost()
    {
        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001",
                AuthorityHost = "http://login.microsoftonline.com"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "Contoso",
                    ApplicationId = "12345678",
                    PackagePaths = new[] { @"C:\temp\Contoso.msixupload" },
                    Commit = false
                }
            }
        };

        using var service = new StoreSubmissionService(new NullLogger());
        var errors = service.Validate(spec, Path.Combine(Path.GetTempPath(), "store.submit.json"));

        Assert.Contains(errors, error => error.Contains("authorityHost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DesktopInstallerRequiresSellerId()
    {
        var spec = new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                TenantId = "tenant-001",
                ClientId = "client-001",
                ClientSecret = "secret-001"
            },
            Targets = new[]
            {
                new StoreSubmissionTarget
                {
                    Name = "DesktopContoso",
                    Provider = StoreSubmissionProviderKind.DesktopInstaller,
                    ApplicationId = "desktop-123",
                    DesktopPackages = new[]
                    {
                        new StoreSubmissionDesktopPackage
                        {
                            PackageUrl = "https://downloads.contoso.test/PowerForgeSetup.msi",
                            Languages = new[] { "en-us" },
                            Architectures = new[] { "X64" },
                            IsSilentInstall = true,
                            PackageType = "msi"
                        }
                    }
                }
            }
        };

        using var service = new StoreSubmissionService(new NullLogger());
        var errors = service.Validate(spec, Path.Combine(Path.GetTempPath(), "desktop-store-submit.json"));

        Assert.Contains(errors, error => error.Contains("Authentication.SellerId", StringComparison.Ordinal));
    }

    private static StoreSubmissionSpec CreateSpec(StoreSubmissionTarget target, string? accessToken = null)
    {
        return new StoreSubmissionSpec
        {
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                AccessToken = accessToken ?? "test-token"
            },
            Targets = new[] { target }
        };
    }

    private static string CreatePackageFile(string rootPath, string fileName)
    {
        var path = Path.Combine(rootPath, fileName);
        File.WriteAllText(path, "store package", new UTF8Encoding(false));
        return path;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, RecordedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bodyBytes = request.Content is null
                ? null
                : request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
            var bodyText = bodyBytes is null || bodyBytes.Length == 0
                ? null
                : Encoding.UTF8.GetString(bodyBytes);

            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Seller-Account-Id", out var sellerIds) ? sellerIds.SingleOrDefault() : null,
                bodyText,
                bodyBytes);

            Requests.Add(recorded);

            return Task.FromResult(responder(request, recorded));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? SellerId, string? BodyText, byte[]? BodyBytes);

    private sealed class TemporaryScope : IDisposable
    {
        public TemporaryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "StoreSubmission", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
