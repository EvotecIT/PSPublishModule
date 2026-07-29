using System;
using System.Collections.Generic;

namespace PowerForge;

public sealed partial class GitHubReleasePublisher {
    internal static bool TryReserveExistingAssetNameForReplacement(ISet<string> replaceableAssetNames, string fileName) {
        if (replaceableAssetNames is null) throw new ArgumentNullException(nameof(replaceableAssetNames));
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        return replaceableAssetNames.Remove(fileName);
    }

    private static HashSet<string> CreateReplaceableAssetNameSet(IEnumerable<GitHubReleaseAssetResponse> existingAssets) {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in existingAssets) {
            if (!string.IsNullOrWhiteSpace(asset.Name))
                names.Add(asset.Name!);
        }

        return names;
    }
}
