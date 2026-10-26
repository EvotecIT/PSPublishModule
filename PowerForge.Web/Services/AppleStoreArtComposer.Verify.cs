using System.Text.Json;
using ImageMagick;
using PowerForge;

namespace PowerForge.Web;

public sealed partial class AppleStoreArtComposer
{
    /// <summary>Checks retained composition against the current spec, captions, template, raw captures and exact outputs without rerendering.</summary>
    public AppleStoreArtLineage[] Verify(string specPath, string outputPath, string[]? locales = null, string[]? devices = null)
    {
        specPath = Path.GetFullPath(specPath); Unlinked(specPath);
        var specBytes = File.ReadAllBytes(specPath);
        var spec = JsonSerializer.Deserialize<AppleStoreArtSpec>(specBytes, Json) ?? throw new ArgumentException("Empty store art spec.");
        ValidateSpec(spec);
        locales = Select(spec.Locales, locales, "locale");
        devices = Select(spec.Devices, devices, "device");
        var baseDirectory = Path.GetDirectoryName(specPath)!;
        var root = Path.GetFullPath(Path.Combine(baseDirectory, spec.CaptureRoot)); Unlinked(root, root);
        var output = Inside(root, Path.GetFullPath(outputPath));
        if (output == root) throw new ArgumentException("Store art output must differ from CaptureRoot.");
        var lineagePath = Inside(root, Path.Combine(output, "store-art.lineage.json"));
        var entries = JsonSerializer.Deserialize<AppleStoreArtLineage[]>(File.ReadAllBytes(lineagePath), Json)
            ?? throw new InvalidOperationException("Missing composition lineage.");
        var templateHash = TemplateHash();
        var themeStyle = Asset("theme-base.css") + ThemeCss(spec.Theme);
        var captionFonts = CaptionFonts(themeStyle);
        var specHash = Hash(specBytes);
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
        var raw = new List<AppleStoreArtInput>();
        foreach (var locale in locales)
        {
            var captionPath = Path.GetFullPath(Path.Combine(baseDirectory, spec.CaptionsPath, $"store-captions.{locale}.json"));
            Unlinked(captionPath);
            var captionBytes = File.ReadAllBytes(captionPath);
            var captionHash = Hash(captionBytes);
            var captions = JsonSerializer.Deserialize<Dictionary<string, string>>(captionBytes, Json)
                ?? throw new ArgumentException($"Empty captions: {locale}");
            var style = CaptionStyle(spec, captions, themeStyle);
            foreach (var device in devices)
            {
                var frames = spec.Frames.Where(frame => Applies(frame, device)).ToArray();
                if (frames.Length == 0) throw new ArgumentException($"No frames select device '{device}'.");
                for (var index = 0; index < frames.Length; index++)
                {
                    var frame = frames[index];
                    var path = Path.GetRelativePath(root, Path.Combine(output, locale, device, $"{index + 1:00}-{frame.Id}.png")).Replace('\\', '/');
                    expectedPaths.Add(path);
                    var entry = entries.SingleOrDefault(item => item.Path == path)
                        ?? throw new InvalidOperationException($"Missing composed frame '{path}'.");
                    var primary = ReadCapture(spec, root, frame.Capture, locale);
                    var watch = frame.Layout == "pair" ? ReadCapture(spec, root, frame.Watch!, locale) : null;
                    var surfaces = frame.Layout == "surfaces" ? frame.Surfaces.Select(reference => ReadCapture(spec, root, reference, locale)).ToArray() : [];
                    var inputs = new[] { primary }.Concat(watch is null ? [] : new[] { watch }).Concat(surfaces)
                        .Select(capture => capture.Input).DistinctBy(input => input.Path).ToArray();
                    var renderHash = device == "watch" ? primary.Input.Sha256
                        : Hash(BuildHtml(frame, locale, device, Displays[device], captions, captionFonts, style, primary, watch, surfaces));
                    if (entry.Frame != frame.Id || entry.Locale != locale || entry.Device != device || entry.Theme != spec.Theme.Name ||
                        entry.SpecSha256 != specHash || entry.CaptionsSha256 != captionHash || entry.TemplateSha256 != templateHash || entry.RenderSha256 != renderHash ||
                        entry.Width != Displays[device].Width || entry.Height != Displays[device].Height ||
                        JsonSerializer.Serialize(entry.Inputs, Json) != JsonSerializer.Serialize(inputs, Json))
                        throw new InvalidOperationException($"Composition no longer matches its spec, captions, template or inputs: {path}");
                    var file = Inside(root, path);
                    using var image = new MagickImage(file);
                    if (image.HasAlpha || entry.PerceptualHash != PerceptualHash(file))
                        throw new InvalidOperationException($"Composition has alpha or a changed perceptual hash: {path}");
                    raw.AddRange(inputs);
                }
            }
        }
        if (entries.Length != expectedPaths.Count || entries.Any(entry => !expectedPaths.Contains(entry.Path)))
            throw new InvalidOperationException("Composition lineage must contain exactly the selected frames.");
        // Review sheets are deliberately outside the upload inventory.
        var actual = EnumerateUploadPngs(output, output, root)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expectedPaths)) throw new InvalidOperationException("Composition output contains missing or unlisted PNG files.");
        AppleStoreArtLineageValidator.Validate(entries, raw.DistinctBy(input => input.Path).ToArray(), root);
        return entries;
    }
    private static IEnumerable<string> EnumerateUploadPngs(string directory, string output, string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            Unlinked(entry, root);
            if (directory == output && Path.GetFileName(entry) == "_review") continue;
            if (Directory.Exists(entry))
            {
                foreach (var file in EnumerateUploadPngs(entry, output, root)) yield return file;
            }
            else if (Path.GetExtension(entry).Equals(".png", StringComparison.OrdinalIgnoreCase)) yield return entry;
        }
    }
}
