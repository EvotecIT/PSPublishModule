namespace PowerForge;

internal static class DotNetPublishSigningProfileResolver
{
    internal static DotNetPublishSignOptions? ResolveConfiguredSignOptions(
        IReadOnlyDictionary<string, DotNetPublishSignOptions>? signingProfiles,
        string? signProfile,
        DotNetPublishSignOptions? sign,
        DotNetPublishSignPatch? signOverrides,
        string contextName)
    {
        if (sign is not null)
            return CloneSignOptions(sign);

        DotNetPublishSignOptions? resolved = null;
        if (!string.IsNullOrWhiteSpace(signProfile))
        {
            if (signingProfiles is null || signingProfiles.Count == 0)
                throw new ArgumentException($"{contextName} references signing profile '{signProfile}', but no SigningProfiles were defined.");

            var key = signProfile!.Trim();
            if (!signingProfiles.TryGetValue(key, out var profile) || profile is null)
                throw new ArgumentException($"{contextName} references unknown signing profile '{key}'.");

            resolved = CloneSignOptions(profile);
        }

        if (signOverrides is not null)
        {
            resolved ??= new DotNetPublishSignOptions();
            ApplySignPatch(resolved, signOverrides);
        }

        return resolved;
    }

    internal static Dictionary<string, DotNetPublishSignOptions>? CloneSigningProfiles(
        IDictionary<string, DotNetPublishSignOptions>? signingProfiles)
    {
        if (signingProfiles is null || signingProfiles.Count == 0)
            return null;

        var clone = new Dictionary<string, DotNetPublishSignOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in signingProfiles)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
                continue;

            clone[kv.Key.Trim()] = CloneSignOptions(kv.Value)!;
        }

        return clone.Count == 0 ? null : clone;
    }

    internal static DotNetPublishSignOptions? CloneSignOptions(DotNetPublishSignOptions? sign)
    {
        if (sign is null) return null;
        return new DotNetPublishSignOptions
        {
            Enabled = sign.Enabled,
            IncludeDlls = sign.IncludeDlls,
            ToolPath = sign.ToolPath,
            OnMissingTool = sign.OnMissingTool,
            OnSignFailure = sign.OnSignFailure,
            Thumbprint = sign.Thumbprint,
            SubjectName = sign.SubjectName,
            TimestampUrl = sign.TimestampUrl,
            Description = sign.Description,
            Url = sign.Url,
            Csp = sign.Csp,
            KeyContainer = sign.KeyContainer
        };
    }

    internal static DotNetPublishSignPatch? CloneSignPatch(DotNetPublishSignPatch? signOverrides)
    {
        if (signOverrides is null) return null;
        return new DotNetPublishSignPatch
        {
            Enabled = signOverrides.Enabled,
            IncludeDlls = signOverrides.IncludeDlls,
            ToolPath = signOverrides.ToolPath,
            OnMissingTool = signOverrides.OnMissingTool,
            OnSignFailure = signOverrides.OnSignFailure,
            Thumbprint = signOverrides.Thumbprint,
            SubjectName = signOverrides.SubjectName,
            TimestampUrl = signOverrides.TimestampUrl,
            Description = signOverrides.Description,
            Url = signOverrides.Url,
            Csp = signOverrides.Csp,
            KeyContainer = signOverrides.KeyContainer
        };
    }

    internal static void ApplySignPatch(DotNetPublishSignOptions sign, DotNetPublishSignPatch patch)
    {
        if (sign is null)
            throw new ArgumentNullException(nameof(sign));
        if (patch is null)
            throw new ArgumentNullException(nameof(patch));

        if (patch.Enabled.HasValue)
            sign.Enabled = patch.Enabled.Value;
        if (patch.IncludeDlls.HasValue)
            sign.IncludeDlls = patch.IncludeDlls.Value;
        if (patch.OnMissingTool.HasValue)
            sign.OnMissingTool = patch.OnMissingTool.Value;
        if (patch.OnSignFailure.HasValue)
            sign.OnSignFailure = patch.OnSignFailure.Value;
        if (patch.ToolPath is not null)
            sign.ToolPath = patch.ToolPath;
        if (patch.Thumbprint is not null)
            sign.Thumbprint = patch.Thumbprint;
        if (patch.SubjectName is not null)
            sign.SubjectName = patch.SubjectName;
        if (patch.TimestampUrl is not null)
            sign.TimestampUrl = patch.TimestampUrl;
        if (patch.Description is not null)
            sign.Description = patch.Description;
        if (patch.Url is not null)
            sign.Url = patch.Url;
        if (patch.Csp is not null)
            sign.Csp = patch.Csp;
        if (patch.KeyContainer is not null)
            sign.KeyContainer = patch.KeyContainer;
    }
}
