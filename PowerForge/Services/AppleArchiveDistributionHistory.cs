using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Recognizes exporter-owned archive bookkeeping, never evidence of upload or release authority.
/// Only an append to Distributions may differ from the approved top-level XML plist.
/// </summary>
internal sealed class AppleArchiveDistributionHistory
{
    private const int MaximumPlistBytes = 1024 * 1024;
    private const int MaximumHistoryEntries = 32;
    private readonly byte[] _approvedBytes;
    private readonly Dictionary<string, XElement> _approvedFields;
    private readonly string _archiveIdentity;
    private readonly string _infoIdentity;

    private AppleArchiveDistributionHistory(string archive, byte[] bytes, Dictionary<string, XElement> fields)
    {
        _approvedBytes = bytes;
        _approvedFields = fields;
        _archiveIdentity = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(archive).Identity;
        _infoIdentity = ExistingFilePathIdentityResolver.Resolve(Path.Combine(archive, "Info.plist"));
    }

    internal static AppleArchiveDistributionHistory? TryCreate(string archive)
    {
        var path = Path.Combine(archive, "Info.plist");
        if (!File.Exists(path))
            return null;
        try
        {
            var bytes = ReadInfoPlist(path);
            var fields = ReadFields(bytes);
            _ = History(fields);
            return new AppleArchiveDistributionHistory(archive, bytes, fields);
        }
        catch (XmlException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal bool HasReplacedInfoPlist(string archive)
    {
        var path = Path.Combine(archive, "Info.plist");
        return File.Exists(path) &&
               (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
               !ExistingFilePathIdentityResolver.Resolve(path).Equals(_infoIdentity, StringComparison.Ordinal);
    }

    internal Validation ValidateAppend(string archive)
    {
        var rootStatus = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(archive);
        if (!rootStatus.Identity.Equals(_archiveIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("The private Apple upload archive root was replaced.");
        var path = Path.Combine(archive, "Info.plist");
        var infoStatus = ExistingFilePathIdentityResolver.ResolveStatus(path);
        var current = ReadFields(ReadInfoPlist(path));
        var beforeHistory = History(_approvedFields);
        var afterHistory = History(current);
        if (afterHistory.Length != beforeHistory.Length + 1 ||
            afterHistory.Length > MaximumHistoryEntries ||
            current.Keys.Any(key => key != "Distributions" && !_approvedFields.ContainsKey(key)) ||
            _approvedFields.Any(pair => pair.Key != "Distributions" &&
                (!current.TryGetValue(pair.Key, out var value) || Canonical(pair.Value) != Canonical(value))) ||
            beforeHistory.Where((value, index) => Canonical(value) != Canonical(afterHistory[index])).Any())
        {
            throw new InvalidOperationException(
                "The private Apple upload archive Info.plist changed beyond a bounded distribution-history append.");
        }
        // The appended dictionary is bounded plist data, not a delivery attestation. Xcode may
        // append success or failure bookkeeping; process/remote receipts still decide delivery.
        if (afterHistory[afterHistory.Length - 1].Name != "dict")
            throw new InvalidOperationException("Apple archive distribution history entries must be plist dictionaries.");
        _ = Canonical(afterHistory[afterHistory.Length - 1]);
        var validation = new Validation(_approvedBytes, infoStatus.MutationIdentity, rootStatus.MutationIdentity);
        validation.ValidateStillSame(archive);
        return validation;
    }

    private static XElement[] History(Dictionary<string, XElement> fields)
    {
        if (!fields.TryGetValue("Distributions", out var value))
            return Array.Empty<XElement>();
        if (value.Name != "array" || value.Elements().Count() > MaximumHistoryEntries)
            throw new InvalidOperationException("Apple archive distribution history must be a bounded plist array.");
        return value.Elements().ToArray();
    }

    private static Dictionary<string, XElement> ReadFields(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumPlistBytes
        });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root?.Name != "plist" || root.Attributes().Count() != 1 || (string?)root.Attribute("version") != "1.0" ||
            root.Elements().Count() != 1 || root.Elements().Single().Name != "dict" ||
            root.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw new InvalidOperationException("Apple archive Info.plist must contain one XML plist dictionary.");
        var fields = Dictionary(root.Elements().Single());
        // Validate the complete grammar, including nested dictionaries and duplicate keys.
        _ = Canonical(root.Elements().Single());
        return fields;
    }

    private static Dictionary<string, XElement> Dictionary(XElement dictionary)
    {
        var values = dictionary.Elements().ToArray();
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (values.Length % 2 != 0)
            throw new InvalidOperationException("Invalid Apple archive plist dictionary.");
        for (var index = 0; index < values.Length; index += 2)
        {
            if (values[index].Name != "key" || values[index].HasElements || values[index].HasAttributes ||
                result.ContainsKey(values[index].Value))
                throw new InvalidOperationException("Invalid or duplicate Apple archive plist dictionary key.");
            result.Add(values[index].Value, values[index + 1]);
        }
        return result;
    }

    private static string Canonical(XElement value, int depth = 0)
    {
        if (depth > 32 || value.HasAttributes || value.Name.Namespace != XNamespace.None)
            throw new InvalidOperationException("Unsupported Apple archive plist structure.");
        static string Frame(string text) => text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + text;
        var name = value.Name.LocalName;
        if ((name == "dict" || name == "array") &&
            value.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw new InvalidOperationException("Unexpected text inside an Apple archive plist container.");
        if (name == "dict")
            return "dict" + string.Concat(Dictionary(value).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Frame(pair.Key) + Frame(Canonical(pair.Value, depth + 1))));
        if (name == "array")
            return "array" + string.Concat(value.Elements().Select(child => Frame(Canonical(child, depth + 1))));
        if (value.HasElements || !(name is "string" or "integer" or "real" or "date" or "data" or "true" or "false"))
            throw new InvalidOperationException("Unsupported Apple archive plist value.");
        return name + Frame(value.Value);
    }

    private static byte[] ReadInfoPlist(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
            ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { path })[0] != 1)
            throw new InvalidOperationException("Apple archive Info.plist must be an unlinked regular file.");
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows() && !ExistingFilePathIdentityResolver.IsRegularUnixFile(path))
            throw new InvalidOperationException("Apple archive Info.plist must be a regular file.");
#endif
        var before = ExistingFilePathIdentityResolver.ResolveStatus(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumPlistBytes ||
            !before.MutationIdentity.Equals(ExistingFilePathIdentityResolver.ResolveStatus(stream.SafeFileHandle).MutationIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("Apple archive Info.plist is oversized or changed while opening it.");
        var bytes = new byte[checked((int)stream.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException("Apple archive Info.plist changed while reading it.");
            offset += read;
        }
        if (!before.MutationIdentity.Equals(ExistingFilePathIdentityResolver.ResolveStatus(path).MutationIdentity, StringComparison.Ordinal) ||
            ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { path })[0] != 1)
            throw new InvalidOperationException("Apple archive Info.plist changed while reading it.");
        return bytes;
    }

    internal sealed class Validation
    {
        private readonly string _infoMutationIdentity;
        private readonly string _rootMutationIdentity;

        internal Validation(byte[] approvedBytes, string infoMutationIdentity, string rootMutationIdentity)
        {
            ApprovedBytes = approvedBytes;
            _infoMutationIdentity = infoMutationIdentity;
            _rootMutationIdentity = rootMutationIdentity;
        }

        internal byte[] ApprovedBytes { get; }

        internal bool MatchesInfo(string archive)
        {
            var path = Path.Combine(archive, "Info.plist");
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                   ExistingFilePathIdentityResolver.CapturePrivateFileMutationIdentity(path, "archive Info.plist")
                       .Equals(_infoMutationIdentity, StringComparison.Ordinal);
        }

        internal bool MatchesRoot(string archive)
            => (File.GetAttributes(archive) & FileAttributes.ReparsePoint) == 0 &&
               ExistingFilePathIdentityResolver.ResolveDirectoryStatus(archive).MutationIdentity.Equals(_rootMutationIdentity, StringComparison.Ordinal);

        internal void ValidateStillSame(string archive)
        {
            if (!MatchesInfo(archive) || !MatchesRoot(archive))
                throw new InvalidOperationException("The private Apple upload archive distribution history changed while it was being validated.");
        }
    }
}
