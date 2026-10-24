using PowerForge;

namespace PowerForge.Web;

public sealed partial class AppleStoreArtComposer
{
    private sealed record Capture(byte[] Bytes, AppleStoreArtInput Input)
    {
        public string Uri => "data:image/png;base64," + Convert.ToBase64String(Bytes);
    }
    private static string BuildHtml(AppleStoreArtFrame frame, string locale, string device, AppleStoreArtDisplay size,
        Dictionary<string, string> captions, string styles, Capture primary, Capture? watch, Capture[] surfaces)
    {
        string Caption(string? key, bool required = false)
        {
            if (key is null && !required) return "";
            if (key is null || !captions.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Locale '{locale}', frame '{frame.Id}': missing caption '{key ?? "headlineKey"}'.");
            return text;
        }
        var headline = Caption(frame.HeadlineKey, true);
        var eyebrow = Caption(frame.EyebrowKey);
        var subline = Caption(frame.SublineKey);
        var header = "<header dir=\"auto\">" + (eyebrow.Length == 0 ? "" : "<p class=\"eyebrow\">" + Escape(eyebrow) + "</p>") +
            "<h1>" + Headline(headline) + "</h1>" +
            (subline.Length == 0 ? "" : "<p class=\"sub\">" + Escape(subline) + "</p>") + "</header>";
        // Widths in the prototype are expressed on each native base canvas.
        var ratio = size.Width / (device == "ipad-13" ? 2064d : device == "mac" ? 2880d : 1320d);
        if (device is "duo-outer" or "duo-inner")
            ratio *= (size.Height / (double)size.Width) / (2868d / 1320);
        var width = (frame.DeviceWidth ?? (frame.Layout switch { "focus" => 900, "pair" => 880, "ipad" => 1720, "mac" => 2400, _ => 1010 })) * ratio;
        string Image(Capture capture) => "<img draggable=\"false\" src=\"" + capture.Uri + "\">";
        string Phone(string extra = "", string cls = "") => "<div class=\"phone " + cls + "\" style=\"--w:" + Number(width) +
            "px;transform:rotate(0deg)\"><div class=\"phone-screen\">" + Image(primary) +
            (frame.Island ? "<i class=\"island\"></i>" : "") + "</div>" + extra + "</div>";
        var layoutClass = frame.Layout == "mac" ? "macl" : frame.Layout;
        var content = frame.Layout switch
        {
            "hero" or "statement" => Phone(),
            "focus" => Phone(Lens(frame, width, primary)),
            "pair" => Phone(cls: "back") + "<div class=\"watch front\" style=\"--w:" + Number(frame.WatchWidth * ratio) +
                "px\"><i class=\"crown\"></i><div class=\"watch-screen\">" + Image(watch!) + "</div></div>",
            "ipad" => "<div class=\"tablet\" style=\"--w:" + Number(width) + "px\"><div class=\"tablet-screen\">" + Image(primary) + "</div></div>",
            "mac" => "<div class=\"mac\" style=\"--w:" + Number(width) + "px\">" + Image(primary) + "</div>",
            "surfaces" => string.Join("", new[] { primary }.Concat(surfaces).Select(c => "<div class=\"surface\">" + Image(c) + "</div>")),
            _ => throw new ArgumentException($"Unsupported layout '{frame.Layout}'.")
        };
        var body = "<div class=\"stage " + layoutClass + "\">" + header + "<div class=\"dev-wrap" +
            (frame.Layout == "focus" ? " dim" : "") + "\">" + content + "</div></div>";
        return Asset("frame.html").Replace("{{locale}}", Escape(locale)).Replace("{{mood}}", Escape(frame.Mood))
            .Replace("{{device}}", device == "mac" ? "mac-size" : device).Replace("{{body}}", body)
            .Replace("{{styles}}", ":root{--W:" + size.Width + "px;--H:" + size.Height + "px;--u:" + Number(size.Width / 1320d) + "}" + styles);
    }
    private static string Lens(AppleStoreArtFrame frame, double width, Capture capture)
    {
        var lens = frame.Lens ?? throw new ArgumentException($"Focus frame '{frame.Id}' requires a lens.");
        var rect = lens.Rect;
        if (rect.Length != 4 || rect.Any(v => !double.IsFinite(v) || v < 0) || rect[2] <= 0 || rect[3] <= 0 ||
            rect[0] + rect[2] > capture.Input.Width || rect[1] + rect[3] > capture.Input.Height ||
            !double.IsFinite(lens.Scale) || lens.Scale <= 0 || lens.Scale > 8 || lens.At.Length != 2 || lens.At.Any(v => !double.IsFinite(v)))
            throw new ArgumentException($"Invalid source lens rectangle for frame '{frame.Id}'.");
        var bezel = width * .034; var k = (width - 2 * bezel) / capture.Input.Width;
        var lw = rect[2] * k * lens.Scale; var lh = rect[3] * k * lens.Scale;
        var lx = bezel + (rect[0] + rect[2] / 2) * k - lw / 2 + lens.At[0] * width / (frame.DeviceWidth ?? 900);
        var ly = bezel + (rect[1] + rect[3] / 2) * k - lh / 2 + lens.At[1] * width / (frame.DeviceWidth ?? 900);
        return "<div class=\"lens\" style=\"--lw:" + Number(lw) + "px;--lh:" + Number(lh) + "px;--lx:" + Number(lx) +
            "px;--ly:" + Number(ly) + "px\"><img src=\"" + capture.Uri + "\" style=\"width:" +
            Number(capture.Input.Width * k * lens.Scale) + "px;left:" + Number(-rect[0] * k * lens.Scale) +
            "px;top:" + Number(-rect[1] * k * lens.Scale) + "px\"></div>";
    }
}
