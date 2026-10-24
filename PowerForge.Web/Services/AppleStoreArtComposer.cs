using System.Collections.ObjectModel;
using System.Text.Json;
using HtmlTinkerX;
using ImageMagick;
using PowerForge;

namespace PowerForge.Web;

/// <summary>Composes offline store artwork using the shared browser and unchanged real captures.</summary>
public sealed partial class AppleStoreArtComposer
{
    /// <summary>Supported local output geometries. Duo keys do not imply upload API support.</summary>
    public static IReadOnlyDictionary<string, AppleStoreArtDisplay> Displays { get; } =
        new ReadOnlyDictionary<string, AppleStoreArtDisplay>(new Dictionary<string, AppleStoreArtDisplay>(StringComparer.Ordinal)
        {
            ["iphone-69"] = new(1320, 2868), ["iphone-63"] = new(1206, 2622),
            ["duo-outer"] = new(1398, 2034), ["duo-inner"] = new(2007, 2853),
            ["ipad-13"] = new(2064, 2752), ["mac"] = new(2880, 1800), ["watch"] = new(416, 496)
        });

    /// <summary>
    /// Renders into a new, empty directory inside CaptureRoot. Publishes only after every frame passes.
    /// Returns lineage relative to CaptureRoot. No network is used by rendered pages.
    /// </summary>
    /// <param name="specPath">Per-app JSON specification.</param>
    /// <param name="outputPath">Fresh output directory inside the capture inventory root.</param>
    /// <param name="browserExecutablePath">Optional qualified Chromium executable; otherwise HtmlTinkerX uses its shared installation.</param>
    /// <param name="cancellationToken">Cancels composition and tears down the owned browser session.</param>
    public async Task<AppleStoreArtLineage[]> ComposeAsync(string specPath, string outputPath,
        string? browserExecutablePath = null, CancellationToken cancellationToken = default)
    {
        specPath = Path.GetFullPath(specPath); Unlinked(specPath);
        var specBytes = File.ReadAllBytes(specPath);
        var spec = JsonSerializer.Deserialize<AppleStoreArtSpec>(specBytes, Json) ?? throw new ArgumentException("Empty store art spec.");
        var baseDirectory = Path.GetDirectoryName(specPath)!;
        var captureRoot = Path.GetFullPath(Path.Combine(baseDirectory, spec.CaptureRoot)); Unlinked(captureRoot, captureRoot);
        if (!Directory.Exists(captureRoot)) throw new DirectoryNotFoundException(captureRoot);
        var output = Inside(captureRoot, Path.GetFullPath(outputPath));
        if (output == captureRoot || File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new IOException("Store art output must be a fresh, empty directory distinct from CaptureRoot.");
        ValidateSpec(spec);
        var themeStyle = Asset("theme-base.css") + ThemeCss(spec.Theme);
        var templateHash = TemplateHash();
        var staging = Inside(captureRoot, output + ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var outputs = new List<AppleStoreArtLineage>();
        try
        {
            foreach (var locale in spec.Locales)
            {
                var captionPath = Path.GetFullPath(Path.Combine(baseDirectory, spec.CaptionsPath, $"store-captions.{locale}.json"));
                Unlinked(captionPath);
                var captionBytes = File.ReadAllBytes(captionPath);
                var captions = JsonSerializer.Deserialize<Dictionary<string, string>>(captionBytes, Json) ?? throw new ArgumentException($"Empty captions: {locale}");
                var includeCjk = captions.Values.Any(text => text.Any(c => c >= '\u2e80' || c is >= '\u1100' and <= '\u11ff')) ||
                    spec.Theme.Tokens.Values.Any(value => value.Contains("Noto", StringComparison.Ordinal));
                var style = FontCss(includeCjk) + themeStyle;
                foreach (var device in spec.Devices)
                {
                    var size = Displays[device];
                    var frames = spec.Frames.Where(f => Applies(f, device)).ToArray();
                    if (frames.Length == 0) throw new ArgumentException($"No frames select device '{device}'.");
                    if (device == "watch" && frames.Length > 5) throw new ArgumentException("Watch permits at most five raw frames.");
                    var folder = Path.Combine(staging, locale, device); Directory.CreateDirectory(folder);
                    for (var index = 0; index < frames.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var frame = frames[index];
                        try
                        {
                            var primary = ReadCapture(spec, captureRoot, frame.Capture, locale);
                            var watch = frame.Layout == "pair" ? ReadCapture(spec, captureRoot, frame.Watch!, locale) : null;
                            var surfaces = frame.Layout == "surfaces" ? frame.Surfaces.Select(s => ReadCapture(spec, captureRoot, s, locale)).ToArray() : [];
                            var name = $"{index + 1:00}-{frame.Id}.png";
                            var file = Path.Combine(folder, name);
                            string renderHash, renderer;
                            if (device == "watch")
                            {
                                if (frame.Layout != "watch" || primary.Input.Width != size.Width || primary.Input.Height != size.Height)
                                    throw new InvalidOperationException("Watch output requires a watch layout and an exact 416x496 raw PNG.");
                                using var image = new MagickImage(primary.Bytes);
                                if (!image.IsOpaque) throw new InvalidOperationException("Watch passthrough PNG must be opaque; transparent captures cannot preserve RGB pixels in a no alpha output.");
                                File.WriteAllBytes(file, primary.Bytes);
                                if (image.HasAlpha) NormalizePng(file, size);
                                renderHash = primary.Input.Sha256; renderer = "passthrough";
                            }
                            else
                            {
                                var html = BuildHtml(frame, locale, device, size, captions, style, primary, watch, surfaces);
                                renderHash = Hash(html);
                                var htmlFile = Path.Combine(staging, "frame.html"); File.WriteAllText(htmlFile, html);
                                try
                                {
                                    await using var session = await HtmlBrowser.OpenSessionAsync(new Uri(htmlFile).AbsoluteUri,
                                        new HtmlBrowserLaunchOptions
                                        {
                                            Browser = HtmlBrowserEngine.Chromium, Headless = true,
                                            BrowserExecutablePath = browserExecutablePath, ViewportWidth = size.Width, ViewportHeight = size.Height,
                                            DeviceScaleFactor = 1, Locale = locale, Timezone = "UTC", Timeout = 30000
                                        }, cancellationToken).ConfigureAwait(false);
                                    await HtmlBrowser.EvaluateAsync<string>(session, Asset("fit.js"), cancellationToken).ConfigureAwait(false);
                                    await HtmlBrowser.CaptureScreenshotAsync(session.Page, file,
                                        new ScreenshotOptions { FullPage = false }, cancellationToken).ConfigureAwait(false);
                                    renderer = "Chromium " + session.Page.Context.Browser!.Version;
                                }
                                finally { File.Delete(htmlFile); }
                                NormalizePng(file, size);
                            }
                            var inputs = new[] { primary }.Concat(watch is null ? [] : new[] { watch }).Concat(surfaces)
                                .Select(c => c.Input).DistinctBy(i => i.Path).ToArray();
                            var lineage = new AppleStoreArtLineage
                            {
                                Path = Path.GetRelativePath(captureRoot, Path.Combine(output, locale, device, name)).Replace('\\', '/'),
                                Sha256 = Hash(File.ReadAllBytes(file)), Width = size.Width, Height = size.Height,
                                PerceptualHash = PerceptualHash(file), Frame = frame.Id, Locale = locale, Device = device, Theme = spec.Theme.Name,
                                SpecSha256 = Hash(specBytes), CaptionsSha256 = Hash(captionBytes), TemplateSha256 = templateHash,
                                RenderSha256 = renderHash, Renderer = renderer, Inputs = inputs
                            };
                            outputs.Add(lineage);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            throw new InvalidOperationException($"Store art locale '{locale}', frame '{frame.Id}', device '{device}': {ex.Message}", ex);
                        }
                    }
                    ContactSheet(folder, Path.Combine(staging, "_review", locale + "-" + device + ".png"));
                }
            }
            File.WriteAllText(Path.Combine(staging, "store-art.lineage.json"), JsonSerializer.Serialize(outputs.ToArray(), Json) + "\n");
            // Remove only an empty destination. Every generated file is staged until all locales pass.
            Unlinked(output, captureRoot);
            if (Directory.Exists(output)) Directory.Delete(output);
            Directory.Move(staging, output);
            return outputs.ToArray();
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static bool Applies(AppleStoreArtFrame frame, string device) => frame.Devices.Length > 0
        ? frame.Devices.Contains(device, StringComparer.Ordinal)
        : frame.Layout switch { "watch" => device == "watch", "ipad" => device == "ipad-13", "mac" => device == "mac", _ => device is not ("watch" or "ipad-13" or "mac") };

    private static void ValidateSpec(AppleStoreArtSpec spec)
    {
        if (spec.SchemaVersion != 1 || spec.Locales.Length == 0 || spec.Devices.Length == 0 || spec.Frames.Length == 0)
            throw new ArgumentException("Store art schemaVersion 1 requires locales, devices, and frames.");
        foreach (var locale in spec.Locales) SafeId(locale, "locale");
        foreach (var device in spec.Devices) if (!Displays.ContainsKey(device)) throw new ArgumentException($"Unknown store art device '{device}'.");
        foreach (var frame in spec.Frames)
        {
            SafeId(frame.Id, "frame id");
            if (frame.Layout is not ("hero" or "focus" or "pair" or "surfaces" or "statement" or "ipad" or "mac" or "watch"))
                throw new ArgumentException($"Unknown store art layout '{frame.Layout}'.");
            if (frame.Mood is not ("" or "light" or "dark") || frame.DeviceWidth is <= 0 or > 6000 ||
                (frame.DeviceWidth.HasValue && !double.IsFinite(frame.DeviceWidth.Value)) || !double.IsFinite(frame.WatchWidth) || frame.WatchWidth <= 0 || frame.WatchWidth > 2000)
                throw new ArgumentException($"Invalid options for frame '{frame.Id}'.");
            if (frame.Devices.Any(d => !spec.Devices.Contains(d, StringComparer.Ordinal))) throw new ArgumentException($"Frame '{frame.Id}' selects a device absent from devices.");
            if (!spec.Devices.Any(d => Applies(frame, d))) throw new ArgumentException($"Frame '{frame.Id}' has no output device.");
        }
        if (spec.Frames.Select(f => f.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != spec.Frames.Length ||
            spec.Locales.Distinct(StringComparer.OrdinalIgnoreCase).Count() != spec.Locales.Length || spec.Devices.Distinct().Count() != spec.Devices.Length)
            throw new ArgumentException("Frame ids, locales, and devices must be unique.");
    }

    private static Capture ReadCapture(AppleStoreArtSpec spec, string root, string reference, string locale)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A capture reference is required.");
        spec.Captures.TryGetValue(reference + "@" + locale, out var entry);
        if (entry is null) spec.Captures.TryGetValue(reference, out entry);
        if (entry?.Locale is not null && entry.Locale != locale) throw new InvalidOperationException($"Capture '{reference}' locale differs from '{locale}'.");
        var file = Inside(root, (entry?.File ?? reference).Replace("{locale}", locale, StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(file);
        using var image = new MagickImage(bytes);
        if (image.Format != MagickFormat.Png || image.Width == 0 || image.Height == 0 || image.Width > 12000 || image.Height > 12000)
            throw new InvalidOperationException($"Raw capture must be a bounded PNG: {reference}");
        return new Capture(bytes, new AppleStoreArtInput { Path = Path.GetRelativePath(root, file).Replace('\\', '/'),
            Sha256 = Hash(bytes), Width = (int)image.Width, Height = (int)image.Height, Scenario = entry?.Scenario });
    }
}
