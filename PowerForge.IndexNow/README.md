# PowerForge.IndexNow

Submit public website URLs to IndexNow from a .NET 8 or later application. The library owns URL normalization, host batching, bounded response diagnostics, retries, and cancellation. Applications own the verification key file, the list of changed URLs, and durable delivery state.

```csharp
using PowerForge.IndexNow;

IndexNowSubmissionResult result = await IndexNowSubmitter.SubmitAsync(
    new IndexNowSubmissionOptions {
        Key = verificationKey,
        KeyLocation = "https://example.com/" + verificationKey + ".txt",
        Urls = ["https://example.com/news/updated-page/"],
        RetryCount = 2
    }, cancellationToken: stoppingToken);

if (!result.Success) {
    // Retain the pending URLs and inspect result.Errors before retrying.
}
```

The default transport disables redirects and disposes its own HTTP client. A supplied `HttpClient` remains owned by the caller; configure its transport to disable redirects. `DryRun` constructs and validates batches without sending requests. The synchronous `Submit` entry point serves command-line callers.

The library has no package dependencies. The PowerForge website CLI uses this same engine for its `indexnow` pipeline task.
