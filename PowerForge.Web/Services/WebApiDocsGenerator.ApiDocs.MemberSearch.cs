namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // Keep the type catalog compatible with existing clients. Members have their own
    // catalog and share the renderer's anchor assignment, including overloads.
    private static void WriteMemberSearchCatalog(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
        var anchors = BuildMemberRenderInfoMap(types);
        var entries = new List<Dictionary<string, object?>>();
        // The simple renderer has no member-anchor contract. Clear an earlier
        // docs catalog when switching templates instead of advertising broken links.
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var type in template is "docs" or "sidebar" ? types : Array.Empty<ApiTypeModel>())
        {
            if (IsPowerShellCommandType(type)) continue;
            foreach (var member in EnumerateUsageMembers(type).Where(member => !member.IsInherited))
            {
                if (!anchors.TryGetValue(member, out var anchor)) continue;
                var name = string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName;
                var receiver = member.IsExtension ? member.Parameters.FirstOrDefault()?.Type : null;
                entries.Add(new Dictionary<string, object?>
                {
                    ["title"] = $"{type.Name}.{name}",
                    ["aliases"] = new[] { member.Name, name, $"{type.FullName}.{name}",
                        string.IsNullOrWhiteSpace(receiver) ? null : $"{receiver}.{name}" }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray(),
                    ["summary"] = member.Summary ?? string.Empty,
                    ["kind"] = member.IsExtension ? "extension" : anchor.MemberKind,
                    ["namespace"] = type.Namespace,
                    ["assembly"] = type.Assembly ?? string.Empty,
                    ["packageId"] = options.PackageId ?? type.Assembly ?? string.Empty,
                    ["declaringType"] = member.DeclaringType ?? type.FullName,
                    ["receiverType"] = receiver,
                    ["signature"] = member.Signature,
                    ["slug"] = type.Slug,
                    ["anchor"] = GetMemberSearchAnchor(anchor.AnchorId),
                    ["url"] = BuildDocsTypeUrl(options.BaseUrl, type.Slug) + "#" + GetMemberSearchAnchor(anchor.AnchorId)
                });
            }
        }
        WriteJson(Path.Combine(outputPath, "members.json"), entries);
    }

    // Preserve legacy signature IDs in HTML while keeping discovery links compact.
    // A distinct prefix and 96-bit digest separate overloads without route changes.
    private static string GetMemberSearchAnchor(string anchor) => anchor.Length <= 200 ? anchor :
        "member-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(anchor)))[..24].ToLowerInvariant();
}
